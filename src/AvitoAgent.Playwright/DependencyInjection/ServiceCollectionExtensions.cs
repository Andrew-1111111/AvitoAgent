using AvitoAgent.Playwright.Browser;
using AvitoAgent.Playwright.Interfaces;
using AvitoAgent.Playwright.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Playwright.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlaywright(this IServiceCollection services)
    {
        services
            .AddOptions<PlaywrightOptions>()
            .BindConfiguration(PlaywrightOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<PlaywrightOptions>, PlaywrightOptionsValidator>();
        services.AddSingleton<IBrowserManager, BrowserManager>();

        return services;
    }
}
