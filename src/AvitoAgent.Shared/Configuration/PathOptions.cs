namespace AvitoAgent.Shared.Configuration;

public sealed class PathOptions
{
    public const string SectionName = "Paths";

    public string Data { get; set; } = "data";

    public string Logs { get; set; } = "logs";

    public string Prompts { get; set; } = "prompts";
}
