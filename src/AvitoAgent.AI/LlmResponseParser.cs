using System.Text.Json;

using System.Text.Json.Serialization;

using System.Text.RegularExpressions;

using AvitoAgent.AI.Models;



namespace AvitoAgent.AI;



internal static partial class LlmResponseParser

{

    private static readonly JsonSerializerOptions JsonOptions = new()

    {

        PropertyNameCaseInsensitive = true,

    };



    public static LlmAnalysisResult Parse(string content)

    {

        var json = ExtractJson(content);



        try

        {

            var parsed = JsonSerializer.Deserialize<LlmAnalysisResult>(json, JsonOptions);

            return parsed

                ?? throw new InvalidOperationException("Не удалось разобрать JSON-ответ LM Studio.");

        }

        catch (JsonException ex)

        {

            throw new InvalidOperationException(

                "Ответ LM Studio содержит невалидный JSON: " + Preview(json),

                ex

            );

        }

    }



    private static string ExtractJson(string content)

    {

        var trimmed = Normalize(content);



        if (string.IsNullOrWhiteSpace(trimmed))

        {

            throw new InvalidOperationException("Ответ LM Studio не содержит JSON.");

        }



        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))

        {

            return trimmed;

        }



        var fenced = MarkdownJsonRegex().Match(trimmed);

        if (fenced.Success)

        {

            return fenced.Groups[1].Value.Trim();

        }



        var match = JsonBlockRegex().Match(trimmed);

        if (match.Success)

        {

            return match.Value;

        }



        var start = trimmed.IndexOf('{');

        var end = trimmed.LastIndexOf('}');

        if (start >= 0 && end > start)

        {

            return trimmed[start..(end + 1)];

        }



        throw new InvalidOperationException(

            "Ответ LM Studio не содержит JSON. Фрагмент: " + Preview(trimmed)

        );

    }



    private static string Normalize(string content)

    {

        var text = QwenThinking.StripThinkBlocks(content).Trim();



        // Незакрытый <think>… — выкидываем до конца блока или весь хвост после открывающего тега.

        var openThink = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);

        if (openThink >= 0)

        {

            var closeThink = text.IndexOf(

                "</think>",

                openThink,

                StringComparison.OrdinalIgnoreCase

            );

            text =

                closeThink >= 0

                    ? (text[..openThink] + text[(closeThink + "</think>".Length)..]).Trim()

                    : text[..openThink].Trim();

        }



        // Частый мусор вокруг JSON.

        if (text.StartsWith("```", StringComparison.Ordinal))

        {

            var fenced = MarkdownJsonRegex().Match(text);

            if (fenced.Success)

            {

                return fenced.Groups[1].Value.Trim();

            }

        }



        return text.Trim().Trim('`').Trim();

    }



    private static string Preview(string text)

    {

        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();

        if (flat.Length <= 240)

        {

            return flat;

        }



        return flat[..240] + "…";

    }



    [GeneratedRegex(@"\{[\s\S]*\}", RegexOptions.Singleline)]

    private static partial Regex JsonBlockRegex();



    [GeneratedRegex(

        @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",

        RegexOptions.IgnoreCase | RegexOptions.Singleline

    )]

    private static partial Regex MarkdownJsonRegex();

}



internal sealed class LlmAnalysisResult

{

    [JsonPropertyName("isAuthentic")]

    public bool? IsAuthentic { get; init; }



    [JsonPropertyName("authenticityScore")]

    public int AuthenticityScore { get; init; }



    [JsonPropertyName("brand")]

    public string? Brand { get; init; }



    [JsonPropertyName("model")]

    public string? Model { get; init; }



    [JsonPropertyName("category")]

    public string? Category { get; init; }



    [JsonPropertyName("condition")]

    public string? Condition { get; init; }



    [JsonPropertyName("score")]

    public int Score { get; init; }



    [JsonPropertyName("isRelevant")]

    public bool IsRelevant { get; init; }



    [JsonPropertyName("reason")]

    public string? Reason { get; init; }



    [JsonPropertyName("detectedFeatures")]

    public IReadOnlyList<string>? DetectedFeatures { get; init; }



    [JsonPropertyName("counterfeitIndicators")]

    public IReadOnlyList<string>? CounterfeitIndicators { get; init; }



    [JsonPropertyName("confidence")]

    public double Confidence { get; init; }

}


