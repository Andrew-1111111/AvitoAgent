using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AvitoAgent.Infrastructure.Http;

public static class NetworkInterfaceResolver
{
    /// <summary>
    /// Разрешает IP или имя адаптера в локальный unicast-адрес. Пустая строка - null (ОС сама выбирает).
    /// </summary>
    public static bool TryResolve(string? value, out IPAddress? address, out string error)
    {
        address = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var key = value.Trim();

        if (IPAddress.TryParse(key, out var parsed))
        {
            if (!IsAssignedLocally(parsed))
            {
                error =
                    $"Адрес {key} не назначен ни одному локальному интерфейсу. "
                    + $"Доступны: {FormatAvailable()}.";
                return false;
            }

            address = parsed;
            return true;
        }

        var match = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(static ni => ni.OperationalStatus == OperationalStatus.Up)
            .FirstOrDefault(ni =>
                ni.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
                || ni.Description.Equals(key, StringComparison.OrdinalIgnoreCase)
            );

        if (match is null)
        {
            error =
                $"Сетевой интерфейс «{key}» не найден. "
                + $"Укажите IP или имя адаптера. Доступны: {FormatAvailable()}.";
            return false;
        }

        address = GetPreferredAddress(match);
        if (address is null)
        {
            error = $"У интерфейса «{key}» нет подходящего IPv4/IPv6-адреса.";
            return false;
        }

        return true;
    }

    public static void Bind(Socket socket, IPAddress localAddress)
    {
        socket.Bind(new IPEndPoint(localAddress, 0));
    }

    public static Socket CreateTcpSocket(IPAddress? localAddress)
    {
        if (localAddress is null)
        {
            return new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        }

        var socket = new Socket(localAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        Bind(socket, localAddress);
        return socket;
    }

    private static IPAddress? GetPreferredAddress(NetworkInterface networkInterface)
    {
        var addresses = networkInterface
            .GetIPProperties()
            .UnicastAddresses.Select(static a => a.Address)
            .Where(static a =>
                a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
                && !IPAddress.IsLoopback(a)
                && !a.IsIPv6LinkLocal
            )
            .ToList();

        return addresses.FirstOrDefault(static a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault();
    }

    private static bool IsAssignedLocally(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(static ni => ni.OperationalStatus == OperationalStatus.Up)
            .SelectMany(static ni => ni.GetIPProperties().UnicastAddresses)
            .Any(a => a.Address.Equals(address));
    }

    private static string FormatAvailable()
    {
        var items = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(static ni => ni.OperationalStatus == OperationalStatus.Up)
            .Select(ni =>
            {
                var ip = GetPreferredAddress(ni);
                return ip is null ? ni.Name : $"{ni.Name} ({ip})";
            })
            .ToList();

        return items.Count == 0 ? "(нет активных)" : string.Join(", ", items);
    }
}
