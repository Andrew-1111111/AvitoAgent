using AvitoAgent.Shared.Configuration;
using AvitoAgent.Shared.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Infrastructure.Http;

public static class HttpClientFactoryExtensions
{
    public static IServiceCollection AddInfrastructureHttp(this IServiceCollection services)
    {
        services
            .AddHttpClient("Avito", ConfigureDefaultHeaders)
            .ConfigurePrimaryHttpMessageHandler(CreateAppSocketsHandler)
            .AddStandardResilienceHandler(ConfigureAvitoResilience);

        services
            .AddHttpClient(
                "LmStudio",
                (sp, client) =>
                {
                    var options = sp.GetRequiredService<IOptions<LmStudioOptions>>().Value;
                    var baseUrl = options.BaseUrl;
                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        baseUrl =
                            sp.GetRequiredService<IConfiguration>()["LmStudio:BaseUrl"]
                            ?? "http://localhost:1234/v1/";
                    }

                    client.BaseAddress = new Uri(baseUrl);
                    // Длинные vision-запросы; фактический timeout ещё поднимается в анализаторе.
                    client.Timeout = TimeSpan.FromSeconds(
                        Math.Max(options.RequestTimeoutSeconds, 900)
                    );
                    ConfigureDefaultHeaders(client);
                }
            )
            .ConfigurePrimaryHttpMessageHandler(CreateAppSocketsHandler);

        services
            .AddHttpClient(
                "Telegram",
                client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(120);
                    ConfigureDefaultHeaders(client);
                }
            )
            .ConfigurePrimaryHttpMessageHandler(CreateTelegramSocketsHandler);

        return services;
    }

    private static HttpMessageHandler CreateAppSocketsHandler(IServiceProvider services)
    {
        var socks = services.GetRequiredService<IOptions<Socks5Options>>().Value;
        var network = services.GetRequiredService<IOptions<NetworkOptions>>().Value;
        return CreateSocketsHandler(socks, network.Interface);
    }

    private static HttpMessageHandler CreateTelegramSocketsHandler(IServiceProvider services)
    {
        var telegram = services.GetRequiredService<IOptions<TelegramOptions>>().Value;
        return CreateSocketsHandler(telegram.Socks5, telegram.NetworkInterface);
    }

    private static SocketsHttpHandler CreateSocketsHandler(
        Socks5Options? socks,
        string? networkInterface
    )
    {
        if (
            !NetworkInterfaceResolver.TryResolve(
                networkInterface,
                out var localAddress,
                out var error
            )
        )
        {
            throw new InvalidOperationException(error);
        }

        if (socks is { IsConfigured: true })
        {
            return new SocketsHttpHandler
            {
                ConnectCallback = Socks5ConnectionFactory.CreateCallback(socks, localAddress),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };
        }

        if (localAddress is not null)
        {
            return new SocketsHttpHandler
            {
                ConnectCallback = Socks5ConnectionFactory.CreateDirectCallback(localAddress),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };
        }

        return new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
    }

    private static void ConfigureDefaultHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserConstants.DefaultUserAgent);
    }

    private static void ConfigureAvitoResilience(
        Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions options
    )
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromSeconds(2);
        options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 10;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    }
}
