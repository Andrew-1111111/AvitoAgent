using System.Text;
using System.Text.RegularExpressions;

namespace AvitoAgent.AI;

internal static partial class QwenThinking
{
    public const string NoThinkTag = "/no_think";

    /// <summary>
    /// Prefill в точности как в chat template Qwen3.5 при enable_thinking=false.
    /// API LM Studio часто игнорирует флаги — закрытый think в assistant пропускает reasoning.
    /// </summary>
    public const string Prefill = "<think>\n\n</think>\n\n";

    public static string BuildAssistantPrefill(bool startJsonObject) =>
        startJsonObject ? Prefill + "{" : Prefill;

    public static string DisableInPrompt(string prompt)
    {
        var body = StripJinja(prompt).Trim();
        if (body.StartsWith(NoThinkTag, StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        return NoThinkTag + "\n\n" + body;
    }

    public static string PrefixUserText(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith(NoThinkTag, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return NoThinkTag + "\n" + text;
    }

    public static string StripThinkBlocks(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var withoutBlocks = ThinkBlockRegex().Replace(content, string.Empty);
        withoutBlocks = RedactedThinkRegex().Replace(withoutBlocks, string.Empty);
        return withoutBlocks.Trim();
    }

    private static string StripJinja(string prompt)
    {
        var lines = prompt.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder(prompt.Length);

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (
                trimmed.StartsWith("{%", StringComparison.Ordinal)
                || trimmed.StartsWith("{%-", StringComparison.Ordinal)
            )
            {
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlockRegex();

    [GeneratedRegex(@"<redacted_reasoning>[\s\S]*?</redacted_reasoning>", RegexOptions.IgnoreCase)]
    private static partial Regex RedactedThinkRegex();
}
