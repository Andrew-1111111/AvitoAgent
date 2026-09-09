namespace AvitoAgent.Shared.Configuration;

public sealed class ApplicationPaths
{
    public string Root { get; }
    public string Data { get; }
    public string Logs { get; }
    public string Prompts { get; }

    public ApplicationPaths(string root, PathOptions? paths = null)
    {
        Root = root;
        var options = paths ?? new PathOptions();

        Data = ResolveDirectory(root, options.Data, "data");
        Logs = ResolveDirectory(root, options.Logs, "logs");
        Prompts = ResolveDirectory(root, options.Prompts, "prompts");

        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Prompts);
    }

    public string DatabaseFile => Path.Combine(Data, "avito.db");

    public string BrowserProfile => Path.Combine(Data, "browser-profile");

    private static string ResolveDirectory(string root, string? configured, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(root, value));
    }
}
