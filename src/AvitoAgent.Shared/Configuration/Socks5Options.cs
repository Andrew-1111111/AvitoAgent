namespace AvitoAgent.Shared.Configuration;

public sealed class Socks5Options
{
    public const string SectionName = "Socks5";

    public bool Enabled { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1080;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(Host) && Port is >= 1 and <= 65_535;
}
