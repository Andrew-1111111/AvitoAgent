using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Infrastructure.Http;

internal static class Socks5ConnectionFactory
{
    private const byte Version = 0x05;
    private const byte UserPassVersion = 0x01;
    private const byte MethodNoAuth = 0x00;
    private const byte MethodUserPass = 0x02;
    private const byte MethodNoneAcceptable = 0xFF;
    private const byte CommandConnect = 0x01;
    private const byte AddressIPv4 = 0x01;
    private const byte AddressDomain = 0x03;
    private const byte AddressIPv6 = 0x04;

    public static Func<
        SocketsHttpConnectionContext,
        CancellationToken,
        ValueTask<Stream>
    > CreateCallback(Socks5Options proxy, IPAddress? localAddress)
    {
        var host = proxy.Host.Trim();
        var port = proxy.Port;
        var username = proxy.Username?.Trim() ?? string.Empty;
        var password = proxy.Password ?? string.Empty;

        return (context, cancellationToken) =>
            ConnectAsync(host, port, username, password, localAddress, context, cancellationToken);
    }

    public static Func<
        SocketsHttpConnectionContext,
        CancellationToken,
        ValueTask<Stream>
    > CreateDirectCallback(IPAddress? localAddress)
    {
        return (context, cancellationToken) =>
            ConnectDirectAsync(
                context.DnsEndPoint,
                // Сокет, привязанный к адресу адаптера, до 127.0.0.1 не достучится (LM Studio).
                IsLoopback(context.DnsEndPoint.Host) ? null : localAddress,
                cancellationToken
            );
    }

    private static async ValueTask<Stream> ConnectAsync(
        string proxyHost,
        int proxyPort,
        string username,
        string password,
        IPAddress? localAddress,
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken
    )
    {
        if (IsLoopback(context.DnsEndPoint.Host))
        {
            return await ConnectDirectAsync(
                    context.DnsEndPoint,
                    localAddress: null,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        Socket? socket = null;

        try
        {
            socket = NetworkInterfaceResolver.CreateTcpSocket(localAddress);

            await socket
                .ConnectAsync(proxyHost, proxyPort, cancellationToken)
                .ConfigureAwait(false);
            var stream = new NetworkStream(socket, ownsSocket: true);
            socket = null;

            await HandshakeAsync(
                    stream,
                    username,
                    password,
                    proxyHost,
                    proxyPort,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await RequestConnectAsync(
                    stream,
                    context.DnsEndPoint,
                    proxyHost,
                    proxyPort,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return stream;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            socket?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            socket?.Dispose();
            throw Wrap(ex, proxyHost, proxyPort);
        }
    }

    private static async Task HandshakeAsync(
        Stream stream,
        string username,
        string password,
        string proxyHost,
        int proxyPort,
        CancellationToken cancellationToken
    )
    {
        var useAuth = username.Length > 0;
        if (useAuth)
        {
            await stream
                .WriteAsync(
                    new byte[] { Version, 2, MethodNoAuth, MethodUserPass },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        else
        {
            await stream
                .WriteAsync(new byte[] { Version, 1, MethodNoAuth }, cancellationToken)
                .ConfigureAwait(false);
        }

        var method = new byte[2];
        await ReadExactAsync(stream, method, cancellationToken).ConfigureAwait(false);

        if (method[0] != Version)
        {
            throw new HttpRequestException(
                $"SOCKS5-прокси {proxyHost}:{proxyPort} вернул неверный протокол."
            );
        }

        if (method[1] == MethodNoneAcceptable)
        {
            throw new HttpRequestException(
                $"SOCKS5-прокси {proxyHost}:{proxyPort} не поддерживает аутентификацию."
            );
        }

        if (method[1] == MethodUserPass)
        {
            await AuthenticateAsync(
                    stream,
                    username,
                    password,
                    proxyHost,
                    proxyPort,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return;
        }

        if (method[1] != MethodNoAuth)
        {
            throw new HttpRequestException(
                $"SOCKS5-прокси {proxyHost}:{proxyPort} запросил неподдерживаемый способ входа."
            );
        }

        if (useAuth)
        {
            return;
        }
    }

    private static async Task AuthenticateAsync(
        Stream stream,
        string username,
        string password,
        string proxyHost,
        int proxyPort,
        CancellationToken cancellationToken
    )
    {
        var userBytes = Encoding.UTF8.GetBytes(username);
        var passBytes = Encoding.UTF8.GetBytes(password);
        if (userBytes.Length > 255 || passBytes.Length > 255)
        {
            throw new HttpRequestException(
                $"SOCKS5-прокси {proxyHost}:{proxyPort}: логин и пароль не длиннее 255 байт."
            );
        }

        var request = new byte[3 + userBytes.Length + passBytes.Length];
        request[0] = UserPassVersion;
        request[1] = (byte)userBytes.Length;
        userBytes.CopyTo(request, 2);
        request[2 + userBytes.Length] = (byte)passBytes.Length;
        passBytes.CopyTo(request, 3 + userBytes.Length);

        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        var reply = new byte[2];
        await ReadExactAsync(stream, reply, cancellationToken).ConfigureAwait(false);
        if (reply[1] != 0)
        {
            throw new HttpRequestException(
                $"SOCKS5-прокси {proxyHost}:{proxyPort} отклонил логин или пароль."
            );
        }
    }

    private static async Task RequestConnectAsync(
        Stream stream,
        DnsEndPoint target,
        string proxyHost,
        int proxyPort,
        CancellationToken cancellationToken
    )
    {
        var request = BuildConnectRequest(target);
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        var header = new byte[4];
        await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);

        if (header[0] != Version)
        {
            throw new HttpRequestException(
                $"SOCKS5-прокси {proxyHost}:{proxyPort} вернул неверный протокол."
            );
        }

        if (header[1] != 0)
        {
            throw new HttpRequestException(
                $"SOCKS5-прокси {proxyHost}:{proxyPort} отклонил подключение: {DescribeReply(header[1])}"
            );
        }

        await SkipBindAddressAsync(stream, header[3], cancellationToken).ConfigureAwait(false);
    }

    private static byte[] BuildConnectRequest(DnsEndPoint target)
    {
        if (IPAddress.TryParse(target.Host, out var ip))
        {
            var ipBytes = ip.GetAddressBytes();
            var atyp = ip.AddressFamily == AddressFamily.InterNetworkV6 ? AddressIPv6 : AddressIPv4;
            var request = new byte[6 + ipBytes.Length];
            request[0] = Version;
            request[1] = CommandConnect;
            request[2] = 0;
            request[3] = atyp;
            ipBytes.CopyTo(request, 4);
            BinaryPrimitives.WriteUInt16BigEndian(
                request.AsSpan(4 + ipBytes.Length),
                (ushort)target.Port
            );
            return request;
        }

        var hostBytes = Encoding.ASCII.GetBytes(target.Host);
        if (hostBytes.Length is 0 or > 255)
        {
            throw new HttpRequestException($"Некорректный адрес для SOCKS5: {target.Host}.");
        }

        var domainRequest = new byte[7 + hostBytes.Length];
        domainRequest[0] = Version;
        domainRequest[1] = CommandConnect;
        domainRequest[2] = 0;
        domainRequest[3] = AddressDomain;
        domainRequest[4] = (byte)hostBytes.Length;
        hostBytes.CopyTo(domainRequest, 5);
        BinaryPrimitives.WriteUInt16BigEndian(
            domainRequest.AsSpan(5 + hostBytes.Length),
            (ushort)target.Port
        );
        return domainRequest;
    }

    private static async Task SkipBindAddressAsync(
        Stream stream,
        byte atyp,
        CancellationToken cancellationToken
    )
    {
        var addressLength = atyp switch
        {
            AddressIPv4 => 4,
            AddressIPv6 => 16,
            AddressDomain => await ReadDomainLengthAsync(stream, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new HttpRequestException("SOCKS5-прокси вернул неизвестный тип адреса."),
        };

        var rest = new byte[addressLength + 2];
        await ReadExactAsync(stream, rest, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadDomainLengthAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        var length = new byte[1];
        await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
        return length[0];
    }

    private static async Task ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken
    )
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new HttpRequestException(
                    "SOCKS5-прокси закрыл соединение во время рукопожатия."
                );
            }

            offset += read;
        }
    }

    private static string DescribeReply(byte reply) =>
        reply switch
        {
            1 => "Общий сбой прокси.",
            2 => "Прокси запретил подключение.",
            3 => "Сеть недоступна.",
            4 => "Хост недоступен.",
            5 => "Соединение отклонено.",
            6 => "Истекло время жизни пакета.",
            7 => "Команда не поддерживается.",
            8 => "Тип адреса не поддерживается.",
            _ => $"Код ответа {reply}.",
        };

    private static HttpRequestException Wrap(Exception exception, string proxyHost, int proxyPort)
    {
        if (exception is HttpRequestException http)
        {
            return http;
        }

        if (exception is SocketException socket)
        {
            var reason = socket.SocketErrorCode switch
            {
                SocketError.TimedOut => "нет ответа (таймаут)",
                SocketError.ConnectionRefused => "соединение отклонено",
                SocketError.HostNotFound => "хост не найден",
                SocketError.NetworkUnreachable or SocketError.HostUnreachable => "сеть недоступна",
                _ => socket.Message,
            };

            return new HttpRequestException(
                $"SOCKS5-прокси {proxyHost}:{proxyPort}: {reason}.",
                exception
            );
        }

        if (exception is OperationCanceledException)
        {
            return new HttpRequestException(
                $"Истекло время ожидания SOCKS5-прокси {proxyHost}:{proxyPort}.",
                exception
            );
        }

        return new HttpRequestException(
            $"Ошибка SOCKS5-прокси {proxyHost}:{proxyPort}.",
            exception
        );
    }

    private static async ValueTask<Stream> ConnectDirectAsync(
        DnsEndPoint endpoint,
        IPAddress? localAddress,
        CancellationToken cancellationToken
    )
    {
        var socket = NetworkInterfaceResolver.CreateTcpSocket(localAddress);

        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsLoopback(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
