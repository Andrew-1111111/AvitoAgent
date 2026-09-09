using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AvitoAgent.Infrastructure.Http;

/// <summary>
/// Локальный SOCKS5-прокси, который отправляет исходящие соединения с заданного локального адреса.
/// Нужен браузеру: Chromium не умеет привязываться к сетевому интерфейсу, но понимает --proxy-server.
/// </summary>
public sealed class LocalBindingSocksServer : IAsyncDisposable
{
    private const byte Version = 0x05;
    private const byte MethodNoAuth = 0x00;
    private const byte MethodNoneAcceptable = 0xFF;
    private const byte CommandConnect = 0x01;
    private const byte ReplySucceeded = 0x00;
    private const byte ReplyGeneralFailure = 0x01;
    private const byte ReplyCommandNotSupported = 0x07;
    private const byte AddressIPv4 = 0x01;
    private const byte AddressDomain = 0x03;
    private const byte AddressIPv6 = 0x04;

    private readonly TcpListener _listener;
    private readonly IPAddress _localAddress;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _acceptLoop;

    private LocalBindingSocksServer(TcpListener listener, IPAddress localAddress, ILogger? logger)
    {
        _listener = listener;
        _localAddress = localAddress;
        _logger = logger;
        Endpoint = (IPEndPoint)listener.LocalEndpoint;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
    }

    /// <summary>Адрес, который нужно передать браузеру в --proxy-server.</summary>
    public IPEndPoint Endpoint { get; }

    public static LocalBindingSocksServer Start(IPAddress localAddress, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(localAddress);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new LocalBindingSocksServer(listener, localAddress, logger);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception) when (
                cancellationToken.IsCancellationRequested
            )
            {
                return;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(
                () => HandleClientAsync(client, cancellationToken),
                CancellationToken.None
            );
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            Socket? outbound = null;

            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();

                if (!await NegotiateAsync(stream, cancellationToken))
                {
                    return;
                }

                var target = await ReadRequestAsync(stream, cancellationToken);
                if (target is null)
                {
                    await WriteReplyAsync(stream, ReplyCommandNotSupported, cancellationToken);
                    return;
                }

                outbound = await ConnectBoundAsync(
                    target.Value.Host,
                    target.Value.Port,
                    cancellationToken
                );

                if (outbound is null)
                {
                    await WriteReplyAsync(stream, ReplyGeneralFailure, cancellationToken);
                    return;
                }

                await WriteReplyAsync(stream, ReplySucceeded, cancellationToken);

                await using var outboundStream = new NetworkStream(outbound, ownsSocket: true);
                outbound = null;

                await Task.WhenAll(
                    PumpAsync(stream, outboundStream, cancellationToken),
                    PumpAsync(outboundStream, stream, cancellationToken)
                );
            }
            catch (Exception ex) when (
                ex is IOException
                    or SocketException
                    or EndOfStreamException
                    or ObjectDisposedException
                    or OperationCanceledException
            )
            {
                // Обычный обрыв туннеля — браузер закрывает соединения пачками.
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Локальный SOCKS5-прокси: ошибка обработки соединения.");
            }
            finally
            {
                outbound?.Dispose();
            }
        }
    }

    /// <summary>
    /// Перекачивает одно направление и закрывает приёмнику отправку, чтобы вторая половина
    /// туннеля успела дочитать ответ (иначе HTTP-ответ обрывается).
    /// </summary>
    private static async Task PumpAsync(
        NetworkStream source,
        NetworkStream destination,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await source.CopyToAsync(destination, cancellationToken);
        }
        catch
        {
            // Обрыв любой из сторон — нормальный конец туннеля.
        }
        finally
        {
            try
            {
                destination.Socket.Shutdown(SocketShutdown.Send);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
        }
    }

    private static async Task<bool> NegotiateAsync(
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        var greeting = new byte[2];
        await stream.ReadExactlyAsync(greeting, cancellationToken);

        if (greeting[0] != Version)
        {
            return false;
        }

        var methods = new byte[greeting[1]];
        if (methods.Length > 0)
        {
            await stream.ReadExactlyAsync(methods, cancellationToken);
        }

        if (!methods.Contains(MethodNoAuth))
        {
            await stream.WriteAsync(new byte[] { Version, MethodNoneAcceptable }, cancellationToken);
            return false;
        }

        await stream.WriteAsync(new byte[] { Version, MethodNoAuth }, cancellationToken);
        return true;
    }

    private static async Task<(string Host, int Port)?> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken
    )
    {
        var head = new byte[4];
        await stream.ReadExactlyAsync(head, cancellationToken);

        if (head[0] != Version || head[1] != CommandConnect)
        {
            return null;
        }

        string host;
        switch (head[3])
        {
            case AddressIPv4:
            {
                var raw = new byte[4];
                await stream.ReadExactlyAsync(raw, cancellationToken);
                host = new IPAddress(raw).ToString();
                break;
            }
            case AddressIPv6:
            {
                var raw = new byte[16];
                await stream.ReadExactlyAsync(raw, cancellationToken);
                host = new IPAddress(raw).ToString();
                break;
            }
            case AddressDomain:
            {
                var length = new byte[1];
                await stream.ReadExactlyAsync(length, cancellationToken);
                var raw = new byte[length[0]];
                await stream.ReadExactlyAsync(raw, cancellationToken);
                host = Encoding.ASCII.GetString(raw);
                break;
            }
            default:
                return null;
        }

        var portBytes = new byte[2];
        await stream.ReadExactlyAsync(portBytes, cancellationToken);
        return (host, BinaryPrimitives.ReadUInt16BigEndian(portBytes));
    }

    private async Task<Socket?> ConnectBoundAsync(
        string host,
        int port,
        CancellationToken cancellationToken
    )
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var parsed)
                ? [parsed]
                : await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            return null;
        }

        // Привязка возможна только к адресу того же семейства, что и у интерфейса.
        var candidates = addresses
            .Where(address => address.AddressFamily == _localAddress.AddressFamily)
            .ToArray();

        if (candidates.Length == 0)
        {
            _logger?.LogWarning(
                "Локальный SOCKS5-прокси: у {Host} нет адреса, совместимого с интерфейсом {Address}.",
                host,
                _localAddress
            );
            return null;
        }

        foreach (var address in candidates)
        {
            var socket = NetworkInterfaceResolver.CreateTcpSocket(_localAddress);
            try
            {
                await socket.ConnectAsync(address, port, cancellationToken);
                return socket;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
            }
        }

        return null;
    }

    private static Task WriteReplyAsync(
        NetworkStream stream,
        byte reply,
        CancellationToken cancellationToken
    )
    {
        // VER REP RSV ATYP=IPv4 BND.ADDR=0.0.0.0 BND.PORT=0
        byte[] response = [Version, reply, 0x00, AddressIPv4, 0, 0, 0, 0, 0, 0];
        return stream.WriteAsync(response, cancellationToken).AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        _listener.Stop();

        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException) { }

        _cancellation.Dispose();
    }
}
