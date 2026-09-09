using System.Diagnostics;
using System.Text.RegularExpressions;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Playwright.Options;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvitoAgent.App;

/// <summary>
/// При необходимости входа в Avito в headless: пишет Headless=false и перезапускает процесс.
/// </summary>
public sealed partial class AgentRestartCoordinator(
    IOptions<PlaywrightOptions> playwrightOptions,
    IHostApplicationLifetime lifetime,
    IHostEnvironment environment,
    ApplicationPaths paths,
    ILogger<AgentRestartCoordinator> logger
) : IAgentRestartCoordinator
{
    private readonly PlaywrightOptions _playwright = playwrightOptions.Value;
    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private readonly IHostEnvironment _environment = environment;
    private readonly ApplicationPaths _paths = paths;
    private readonly ILogger<AgentRestartCoordinator> _logger = logger;
    private int _restartStarted;

    public bool IsBrowserHeadless => _playwright.Launch.Headless;

    public bool IsRestarting => Volatile.Read(ref _restartStarted) == 1;

    public async Task<bool> DisableHeadlessAndRestartAsync(
        string reason,
        CancellationToken cancellationToken = default
    )
    {
        if (!_playwright.Launch.Headless)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _restartStarted, 1) == 1)
        {
            return true;
        }

        _logger.LogWarning(
            "Требуется видимый браузер для авторизации ({Reason}). Headless → false, перезапуск агента.",
            reason
        );

        var patched = PatchHeadlessFalse();
        if (!patched)
        {
            _logger.LogError(
                "Не удалось записать Headless=false в appsettings.json — перезапуск отменён."
            );
            Interlocked.Exchange(ref _restartStarted, 0);
            return false;
        }

        if (!TryStartReplacementProcess())
        {
            Interlocked.Exchange(ref _restartStarted, 0);
            return false;
        }

        // Даём новому процессу время стартовать, затем гасим текущий host.
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(1_500, CancellationToken.None);
                }
                catch
                {
                    // ignore
                }

                _lifetime.StopApplication();
            },
            CancellationToken.None
        );

        return true;
    }

    private bool PatchHeadlessFalse()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(_environment.ContentRootPath, "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(_paths.Root, "src", "AvitoAgent.App", "appsettings.json"),
            Path.Combine(_paths.Root, "appsettings.json"),
        };

        var any = false;
        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var text = File.ReadAllText(path);
                var updated = HeadlessTrueRegex().Replace(text, "$1false");
                if (string.Equals(text, updated, StringComparison.Ordinal))
                {
                    // Уже false или ключа нет — считаем ок, если Headless уже false в файле.
                    if (HeadlessFalseRegex().IsMatch(text))
                    {
                        any = true;
                    }

                    continue;
                }

                File.WriteAllText(path, updated);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Записано Headless=false в {Path}", path);
                }
                any = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось обновить {Path}", path);
            }
        }

        return any;
    }

    private bool TryStartReplacementProcess()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                _logger.LogError("Не найден путь к исполняемому файлу агента для перезапуска.");
                return false;
            }

            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            var start = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = Directory.GetCurrentDirectory(),
            };

            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                _logger.LogError("Process.Start вернул null при перезапуске агента.");
                return false;
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Запущен новый процесс агента PID={Pid}, текущий будет остановлен.",
                    process.Id
                );
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось запустить новый процесс агента.");
            return false;
        }
    }

    [GeneratedRegex(
        """("Headless"\s*:\s*)true\b""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex HeadlessTrueRegex();

    [GeneratedRegex(
        """("Headless"\s*:\s*)false\b""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex HeadlessFalseRegex();
}
