using AvitoAgent.Core.Interfaces;
using AvitoAgent.Telegram.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AvitoAgent.Telegram.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramNotifications(this IServiceCollection services)
    {
        services.AddSingleton<INotificationService, TelegramNotificationService>();
        services.AddHostedService<TelegramBotService>();
        return services;
    }
}
