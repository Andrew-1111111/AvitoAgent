using AvitoAgent.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AvitoAgent.Infrastructure.Configuration;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddInfrastructureHttp();
        return services;
    }
}
