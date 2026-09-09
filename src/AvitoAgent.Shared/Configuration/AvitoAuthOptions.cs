namespace AvitoAgent.Shared.Configuration;

public sealed class AvitoAuthOptions
{
    public bool Enabled { get; set; } = true;

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string StorageStateFile { get; set; } = "avito-auth.json";

    public bool ManualLogin { get; set; }

    public int LoginTimeoutMs { get; set; } = 120_000;

    public int FormSearchTimeoutMs { get; set; } = 30_000;
}
