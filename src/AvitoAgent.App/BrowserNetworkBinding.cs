using AvitoAgent.Infrastructure.Http;
using AvitoAgent.Playwright.Options;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvitoAgent.App;

/// <summary>
/// Отправляет трафик браузера через интерфейс из Network:Interface.
/// Chromium не умеет привязываться к адаптеру, поэтому поднимаем локальный SOCKS5,
/// который уже открывает исходящие сокеты с нужного адреса.
/// </summary>
internal sealed class BrowserNetworkBinding(
    IOptions<NetworkOptions> network,
    ILogger<BrowserNetworkBinding> logger
) : IAsyncDisposable
{
    private readonly Lock _gate = new();

    private LocalBindingSocksServer? _server;

    public void Apply(PlaywrightOptions options)
    {
        // Явно заданный Playwright:Proxy важнее: он и так уводит браузер в другую сеть.
        if (options.Proxy.IsConfigured)
        {
            return;
        }

        if (
            !NetworkInterfaceResolver.TryResolve(
                network.Value.Interface,
                out var localAddress,
                out var error
            )
        )
        {
            throw new InvalidOperationException(error);
        }

        if (localAddress is null)
        {
            return;
        }

        LocalBindingSocksServer server;
        lock (_gate)
        {
            server = _server ??= LocalBindingSocksServer.Start(localAddress, logger);
        }

        options.Proxy.Enabled = true;
        options.Proxy.Server = $"socks5://127.0.0.1:{server.Endpoint.Port}";
        options.Proxy.Bypass = "localhost, 127.0.0.1";

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Браузер: исходящие соединения с {Address} через локальный SOCKS5 127.0.0.1:{Port}.",
                localAddress.ToString(),
                server.Endpoint.Port
            );
        }
    }

    public async ValueTask DisposeAsync()
    {
        LocalBindingSocksServer? server;
        lock (_gate)
        {
            server = _server;
            _server = null;
        }

        if (server is not null)
        {
            await server.DisposeAsync();
        }
    }
}
