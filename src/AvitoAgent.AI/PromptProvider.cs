using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Logging;

namespace AvitoAgent.AI;

public sealed class PromptProvider(ApplicationPaths paths, ILogger<PromptProvider> logger)
{
    public const string AuthenticityFileName = "authenticity.txt";

    private readonly ApplicationPaths _paths = paths;
    private readonly ILogger<PromptProvider> _logger = logger;
    private string? _authenticityPrompt;
    private bool _logged;

    public string AuthenticityPromptPath => Path.Combine(_paths.Prompts, AuthenticityFileName);

    public string GetAuthenticityPrompt()
    {
        _authenticityPrompt ??= QwenThinking.DisableInPrompt(ReadPrompt(AuthenticityFileName));
        LogOnce();
        return _authenticityPrompt;
    }

    private string ReadPrompt(string fileName)
    {
        var path = Path.Combine(_paths.Prompts, fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Файл промпта не найден: {path}");
        }

        return File.ReadAllText(path);
    }

    private void LogOnce()
    {
        if (_logged)
        {
            return;
        }

        _logged = true;
        Logging.AiLog.PromptSource(_logger, AuthenticityPromptPath);
    }
}
