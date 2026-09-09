using AvitoAgent.Avito.Interfaces;
using AvitoAgent.Avito.Options;
using AvitoAgent.Avito.Services;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Avito.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAvito(this IServiceCollection services)
    {
        services
            .AddOptions<AvitoOptions>()
            .BindConfiguration(AvitoOptions.SectionName)
            .PostConfigure<IConfiguration>(ApplyLocationSlugs)
            .PostConfigure(ApplyCanonicalFilters)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<AvitoOptions>, AvitoOptionsValidator>();

        services.AddSingleton<ILocationCatalog, AvitoLocationCatalog>();
        services.AddSingleton<BrowserWorkingPageHolder>();
        services.AddSingleton<AvitoAuthService>();
        services.AddSingleton<IAvitoAuthService>(sp => sp.GetRequiredService<AvitoAuthService>());
        services.AddSingleton<IAvitoAuthControl>(sp => sp.GetRequiredService<AvitoAuthService>());
        services.AddHostedService<BrowserLifetimeService>();
        services.AddScoped<IMarketplace, AvitoMarketplace>();

        return services;
    }

    private static void ApplyLocationSlugs(AvitoOptions options, IConfiguration configuration)
    {
        options.Filters ??= new();

        var section = configuration.GetSection("Avito:Filters:LocationSlug");
        var values = section
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();

        if (values.Count == 0 && !string.IsNullOrWhiteSpace(section.Value))
        {
            values.Add(section.Value);
        }

        if (values.Count > 0)
        {
            options.Filters.LocationSlug = [.. values];
        }

        options.Filters.LocationSlug = [.. options.Filters.GetLocationSlugs()];
    }

    private static void ApplyCanonicalFilters(AvitoOptions options)
    {
        options.Filters ??= new();
        var filters = options.Filters;

        filters.SortValues = [.. AvitoSort.AllowedValues];
        filters.ConditionValues = [.. AvitoCondition.AllowedValues];
        filters.SellerTypeValues = [.. AvitoSellerType.AllowedValues];

        if (AvitoSort.TryResolve(filters.Sort, out var sort, out _))
        {
            filters.Sort = sort;
        }

        if (AvitoCondition.TryResolve(filters.Condition, out var condition, out _))
        {
            filters.Condition = condition;
        }

        if (AvitoSellerType.TryResolve(filters.SellerType, out var sellerType, out _))
        {
            filters.SellerType = sellerType;
        }
    }
}
