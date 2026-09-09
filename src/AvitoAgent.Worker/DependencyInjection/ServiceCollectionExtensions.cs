using AvitoAgent.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AvitoAgent.Worker.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentWorker(this IServiceCollection services)
    {
        services.AddSingleton<IParseSession, ParseSession>();
        services.AddHostedService<AgentWorker>();
        return services;
    }
}
