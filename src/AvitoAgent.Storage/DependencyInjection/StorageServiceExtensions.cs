using AvitoAgent.Core.Interfaces;
using AvitoAgent.Storage.Data;
using AvitoAgent.Storage.Repositories;
using AvitoAgent.Storage.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AvitoAgent.Storage.DependencyInjection;

public static class StorageServiceExtensions
{
    public static IServiceCollection AddStorage(this IServiceCollection services)
    {
        // Фабрика подключений к SQLite (схема создаётся при первом открытии).
        services.AddSingleton<AvitoDbConnectionFactory>();

        // Репозиторий объявлений.
        services.AddScoped<IListingRepository, ListingRepository>();
        services.AddScoped<IListingPhotoArchiver, ListingPhotoArchiver>();

        return services;
    }
}
