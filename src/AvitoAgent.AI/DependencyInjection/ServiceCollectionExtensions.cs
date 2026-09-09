using AvitoAgent.AI;
using AvitoAgent.AI.Services;
using AvitoAgent.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AvitoAgent.AI.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAi(this IServiceCollection services)
    {
        services.AddSingleton<PromptProvider>();
        services.AddSingleton<LmStudioModelResolver>();
        services.AddScoped<IProductAnalyzer, LmStudioProductAnalyzer>();

        return services;
    }
}
