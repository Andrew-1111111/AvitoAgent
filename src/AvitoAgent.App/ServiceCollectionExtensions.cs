using AvitoAgent.AI.DependencyInjection;
using AvitoAgent.App.Validation;
using AvitoAgent.Avito.DependencyInjection;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Infrastructure.Configuration;
using AvitoAgent.Playwright.DependencyInjection;
using AvitoAgent.Playwright.Options;
using AvitoAgent.Shared.Configuration;
using AvitoAgent.Storage.DependencyInjection;
using AvitoAgent.Telegram.DependencyInjection;
using AvitoAgent.Worker.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AvitoAgent.App;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddSingleton<IValidateOptions<Socks5Options>, Socks5OptionsValidator>();
        services
            .AddOptions<Socks5Options>()
            .Bind(configuration.GetSection(Socks5Options.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<NetworkOptions>, NetworkOptionsValidator>();
        services
            .AddOptions<NetworkOptions>()
            .Bind(configuration.GetSection(NetworkOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<PathOptions>, PathOptionsValidator>();
        services
            .AddOptions<PathOptions>()
            .Bind(configuration.GetSection(PathOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<LmStudioOptions>, LmStudioOptionsValidator>();
        services
            .AddOptions<LmStudioOptions>()
            .Bind(configuration.GetSection(LmStudioOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<TelegramOptions>, TelegramOptionsValidator>();
        services
            .AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();
        services
            .AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<DebugOptions>, DebugOptionsValidator>();
        services
            .AddOptions<DebugOptions>()
            .Bind(configuration.GetSection(DebugOptions.SectionName))
            .ValidateOnStart();

        services.AddInfrastructure();
        services.AddPlaywright();

        // Network:Interface действует и на браузер: подменяем Playwright:Proxy локальным SOCKS5.
        services.AddSingleton<BrowserNetworkBinding>();
        services
            .AddOptions<PlaywrightOptions>()
            .PostConfigure<BrowserNetworkBinding>(
                static (playwright, binding) => binding.Apply(playwright)
            );

        services.AddStorage();
        services.AddSingleton<IAgentRestartCoordinator, AgentRestartCoordinator>();
        services.AddAvito();
        services.AddAi();
        services.AddTelegramNotifications();
        services.AddAgentWorker();

        // Раньше остальных hosted: перехват закрытия консоли / ProcessExit.
        services.AddHostedService<GracefulShutdownService>();

        return services;
    }
}
