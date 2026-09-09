using System.Runtime.InteropServices;
using AvitoAgent.Avito.Interfaces;
using AvitoAgent.Playwright.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AvitoAgent.App;

/// <summary>
/// Перехватывает закрытие консоли / выход из системы и запускает тот же shutdown, что Ctrl+C.
/// Жёсткий Kill (taskkill /F) ОС убивает процесс мгновенно - код выполнить нельзя;
/// для End Task / закрытия окна консоли Windows даёт несколько секунд.
/// </summary>
public sealed class GracefulShutdownService(
    IHostApplicationLifetime lifetime,
    IBrowserManager browserManager,
    IAvitoAuthService authService,
    ILogger<GracefulShutdownService> logger
) : IHostedService
{
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(12);

    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private readonly IBrowserManager _browserManager = browserManager;
    private readonly IAvitoAuthService _authService = authService;
    private readonly ILogger<GracefulShutdownService> _logger = logger;
    private readonly ManualResetEventSlim _hostStopped = new(false);
    private ConsoleCtrlHandler? _consoleHandler;
    private int _shutdownStarted;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime.ApplicationStopped.Register(static state =>
        {
            ((ManualResetEventSlim)state!).Set();
        }, _hostStopped);

        // Ctrl+C / Ctrl+Break: не даём runtime сразу убить процесс - ждём StopAsync хоста.
        Console.CancelKeyPress += OnCancelKeyPress;

        // Последний шанс при выходе домена (не срабатывает на TerminateProcess).
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        if (OperatingSystem.IsWindows())
        {
            _consoleHandler = OnWindowsConsoleCtrl;
            if (!NativeMethods.SetConsoleCtrlHandler(_consoleHandler, add: true))
            {
                _logger.LogWarning(
                    "Не удалось установить ConsoleCtrlHandler - закрытие окна консоли может не сохранить сессию."
                );
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // true = не завершать процесс сразу; остановим Host сами.
        e.Cancel = true;
        RequestGracefulShutdown($"CancelKeyPress ({e.SpecialKey})");
    }

    private bool OnWindowsConsoleCtrl(CtrlType ctrlType)
    {
        switch (ctrlType)
        {
            case CtrlType.CtrlCEvent:
            case CtrlType.CtrlBreakEvent:
            case CtrlType.CtrlCloseEvent:
            case CtrlType.CtrlLogoffEvent:
            case CtrlType.CtrlShutdownEvent:
                RequestGracefulShutdown($"ConsoleCtrl ({ctrlType})");
                // Блокируем handler, пока Host не завершит StopAsync (сохранение профиля/Telegram).
                _hostStopped.Wait(ShutdownWait);
                return true;
            default:
                return false;
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        // Синхронный дожим, если StopAsync не успел (или обошли ConsoleCtrl).
        try
        {
            RequestGracefulShutdown("ProcessExit");
            FlushBrowserSessionSync();
            _hostStopped.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // ProcessExit не должен бросать.
        }
    }

    private void RequestGracefulShutdown(string reason)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 1)
        {
            return;
        }

        _logger.LogWarning("Запрошено корректное завершение: {Reason}", reason);
        try
        {
            _lifetime.StopApplication();
        }
        catch
        {
            // ignore
        }
    }

    private void FlushBrowserSessionSync()
    {
        try
        {
            _browserManager
                .ClosePersistentSessionAsync(_authService.StorageStatePath, CancellationToken.None)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ProcessExit: не удалось сохранить/закрыть сессию браузера");
        }
    }

    private enum CtrlType : uint
    {
        CtrlCEvent = 0,
        CtrlBreakEvent = 1,
        CtrlCloseEvent = 2,
        CtrlLogoffEvent = 5,
        CtrlShutdownEvent = 6,
    }

    private delegate bool ConsoleCtrlHandler(CtrlType ctrlType);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler? handler, bool add);
    }
}
