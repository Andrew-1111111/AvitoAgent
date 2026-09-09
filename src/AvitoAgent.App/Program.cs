using System.Text;
using AvitoAgent.AI;
using AvitoAgent.Infrastructure.Http;
using AvitoAgent.Playwright.Exceptions;
using AvitoAgent.Playwright.Logging;
using AvitoAgent.Shared;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Serilog.Filters;

namespace AvitoAgent.App;

internal class Program
{
    public static async Task Main(string[] args)
    {
        // Корректный вывод кириллицы в консоли Windows.
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine(@"
╔══════════════════════════════════════════╗
║                                          ║
║            Avito Agent v1.0              ║
║                                          ║
║        AI-агент поиска товаров           ║
║                                          ║
╚══════════════════════════════════════════╝
");

        try
        {
            // Generic Host: конфигурация, DI и фоновые сервисы (Worker, Telegram, браузер).
            var builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings
                {
                    Args = args,
                    ContentRootPath = AppContext.BaseDirectory,
                }
            );

            // Корень репозитория - каталог, где лежит prompts/authenticity.txt.
            var projectRoot = ResolveProjectRoot(builder.Environment.ContentRootPath);
            var pathOptions =
                builder.Configuration.GetSection(PathOptions.SectionName).Get<PathOptions>()
                ?? new PathOptions();
            var paths = new ApplicationPaths(projectRoot, pathOptions);

            // Создаём и настраиваем глобальный экземпляр Serilog.
            var debugOptions =
                builder.Configuration.GetSection(DebugOptions.SectionName).Get<DebugOptions>()
                ?? new DebugOptions();

            const string consoleTemplate =
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
            const string fileTemplate =
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] "
                + "{SourceContext} {Message:lj}{NewLine}{Exception}";

            var loggerConfiguration = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext();

            if (debugOptions.BrowserLog)
            {
                // BrowserDebug только в logs/debug-*.log - не засоряет консоль и основной лог.
                loggerConfiguration = loggerConfiguration
                    .MinimumLevel.Override(
                        PlaywrightBrowserDebug.SourceContext,
                        LogEventLevel.Debug
                    )
                    .WriteTo.Logger(main =>
                        main.Filter.ByExcluding(
                                Matching.FromSource(PlaywrightBrowserDebug.SourceContext)
                            )
                            .WriteTo.Console(outputTemplate: consoleTemplate)
                            .WriteTo.File(
                                Path.Combine(paths.Logs, "avito_agent-.log"),
                                rollingInterval: RollingInterval.Day,
                                retainedFileCountLimit: 30,
                                outputTemplate: fileTemplate
                            )
                    )
                    .WriteTo.Logger(browserDebug =>
                        browserDebug
                            .Filter.ByIncludingOnly(
                                Matching.FromSource(PlaywrightBrowserDebug.SourceContext)
                            )
                            .WriteTo.File(
                                Path.Combine(paths.Logs, "debug-.log"),
                                rollingInterval: RollingInterval.Day,
                                retainedFileCountLimit: 14,
                                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} "
                                    + "{Level:u3}] "
                                    + "{Message:lj}{NewLine}{Exception}"
                            )
                    );
            }
            else
            {
                loggerConfiguration = loggerConfiguration
                    .WriteTo.Console(outputTemplate: consoleTemplate)
                    .WriteTo.File(
                        Path.Combine(paths.Logs, "avito_agent-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        outputTemplate: fileTemplate
                    );
            }

            Log.Logger = loggerConfiguration.CreateLogger();

            // Регистрируем Serilog вместо стандартных провайдеров, иначе сообщения дублируются в консоли.
            builder.Logging.ClearProviders();
            builder.Services.AddSerilog();

            // Регистрация всех модулей приложения (Avito, Worker, Telegram, LM Studio, SQLite и т.д.).
            builder.Services.AddApplicationServices(builder.Configuration);

            // Запас на StopAsync при Ctrl+C / закрытии окна консоли / End Task.
            builder.Services.Configure<HostOptions>(options =>
                options.ShutdownTimeout = TimeSpan.FromSeconds(20)
            );

            // Пути к data/, logs/, prompts/ - один экземпляр на всё приложение.
            builder.Services.AddSingleton(paths);

            // Собираем Host: создаются зависимости, читается appsettings, поднимаются валидаторы опций.
            using var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            // Проверка appsettings (токены, диапазоны, URL и т.п.) до старта фоновых задач.
            if (!ConfigurationErrors.TryValidate(host.Services))
            {
                PromptExit();
                return;
            }

            var checkOnly = args.Any(static argument =>
                string.Equals(argument, "--check-config", StringComparison.OrdinalIgnoreCase)
            );
            if (checkOnly)
            {
                ConfigurationErrors.WriteSnapshot(host.Services);
                return;
            }

            logger.LogInformation("==============================================");

            logger.LogInformation("Avito Agent успешно запущен.");
            if (debugOptions.BrowserLog && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Debug:BrowserLog включён → {Path}",
                    Path.Combine(paths.Logs, "debug-*.log")
                );
            }
            // Опциональный SOCKS5 для исходящего HTTP приложения (не для Playwright).
            var socks5 = host.Services.GetRequiredService<IOptions<Socks5Options>>().Value;
            if (socks5.IsConfigured && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Приложение: SOCKS5 {Host}:{Port} (localhost без прокси).",
                    socks5.Host,
                    socks5.Port
                );
            }

            // Привязка исходящих запросов приложения к выбранному сетевому интерфейсу.
            var network = host.Services.GetRequiredService<IOptions<NetworkOptions>>().Value;
            if (NetworkInterfaceResolver.TryResolve(network.Interface, out var appAddress, out _)
                && appAddress is not null
                && logger.IsEnabled(LogLevel.Information))
            {
                var iface = network.Interface.Trim();
                var address = appAddress.ToString();
                logger.LogInformation(
                    "Приложение: сетевой интерфейс {Interface} ({Address}).",
                    iface,
                    address
                );
            }

            // Отдельные сеть/прокси только для Telegram Bot API.
            var telegram = host.Services.GetRequiredService<IOptions<TelegramOptions>>().Value;
            if (telegram.Socks5 is { IsConfigured: true } && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Telegram: SOCKS5 {Host}:{Port}.",
                    telegram.Socks5.Host,
                    telegram.Socks5.Port
                );
            }

            if (NetworkInterfaceResolver.TryResolve(
                    telegram.NetworkInterface,
                    out var tgAddress,
                    out _)
                && tgAddress is not null
                && logger.IsEnabled(LogLevel.Information))
            {
                var iface = telegram.NetworkInterface.Trim();
                var address = tgAddress.ToString();
                logger.LogInformation(
                    "Telegram: сетевой интерфейс {Interface} ({Address}).",
                    iface,
                    address
                );
            }

            // При включённом LM Studio заранее читаем промпт подлинности (ошибка файла - сразу при старте).
            var lmStudio = host.Services.GetRequiredService<IOptions<LmStudioOptions>>().Value;
            if (lmStudio.Enabled)
            {
                _ = host.Services.GetRequiredService<PromptProvider>().GetAuthenticityPrompt();
            }

            logger.LogInformation("Запущено приложение фоновых задач...");
            logger.LogInformation("==============================================");

            // Блокирует поток до остановки: AgentWorker, Telegram-бот, жизненный цикл браузера.
            await host.RunAsync();
        }
        catch (Exception ex) when (ConfigurationErrors.IsCancellation(ex))
        {
            // Ctrl+C или закрытие браузера во время старта - не ошибка конфигурации.
        }
        catch (Exception ex)
        {
            // Ошибки валидации опций - уже выведены в человекочитаемом виде.
            if (ConfigurationErrors.TryWrite(ex))
            {
                PromptExit();
                return;
            }

            if (ex is PlaywrightException)
            {
                var reason = BrowserErrorText.Describe(ex);
                Log.Fatal("Не удалось запустить браузер: {Reason}", reason);
                Console.WriteLine(Environment.NewLine + "Не удалось запустить браузер.");
                Console.WriteLine(reason);
                PromptExit();
                return;
            }

            // Ожидаемые сбои конфигурации/окружения - без полного стека в консоли.
            if (ex is InvalidOperationException)
            {
                Log.Fatal("Не удалось запустить AvitoAgent: {Message}", ex.Message);
            }
            else
            {
                Log.Fatal(ex, "Критическая ошибка приложения.");
            }

            Console.WriteLine(Environment.NewLine + "Не удалось запустить AvitoAgent.");
            Console.WriteLine(ex.Message);
            PromptExit();
        }
        finally
        {
            // Сбрасываем буферы Serilog, чтобы последние записи успели попасть в файл.
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Ищет корень проекта вверх от ContentRoot (bin/Debug/...), пока не найдёт prompts/authenticity.txt.
    /// Нужен и при запуске из IDE, и при публикации рядом с prompts/.
    /// </summary>
    private static string ResolveProjectRoot(string contentRoot)
    {
        var directory = new DirectoryInfo(contentRoot);

        while (directory is not null)
        {
            var authenticity = Path.Combine(directory.FullName, "prompts", "authenticity.txt");
            if (File.Exists(authenticity))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        // Fallback: работаем из ContentRoot, если промпт лежит рядом с exe или путь задан иначе.
        return contentRoot;
    }

    /// <summary>
    /// Пауза перед закрытием консоли, чтобы успеть прочитать сообщение об ошибке.
    /// </summary>
    private static void PromptExit()
    {
        Console.WriteLine(Environment.NewLine + "Нажмите любую клавишу для выхода...");
        try
        {
            if (!Console.IsInputRedirected)
            {
                Console.ReadKey(intercept: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Нет интерактивной консоли (редирект/служба).
        }
    }
}
