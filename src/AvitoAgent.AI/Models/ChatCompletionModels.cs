using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AvitoAgent.AI.Models;

internal sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; init; }

    /// <summary>LM Studio / Qwen: выключить thinking (несколько имён — разные версии API).</summary>
    [JsonPropertyName("enable_thinking")]
    public bool EnableThinking { get; init; }

    [JsonPropertyName("enableThinking")]
    public bool EnableThinkingCamel { get; init; }

    [JsonPropertyName("thinking")]
    public bool Thinking { get; init; }

    [JsonPropertyName("include_reasoning")]
    public bool IncludeReasoning { get; init; }

    [JsonPropertyName("chat_template_kwargs")]
    public ChatTemplateKwargs ChatTemplateKwargs { get; init; } = ChatTemplateKwargs.Disabled;

    [JsonPropertyName("reasoning")]
    public ReasoningOptions Reasoning { get; init; } = ReasoningOptions.Disabled;

    [JsonPropertyName("response_format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResponseFormat? ResponseFormat { get; init; }

    public static ChatCompletionRequest CreateWithoutThinking(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        double temperature,
        int? maxTokens,
        ResponseFormat? responseFormat
    ) =>
        new()
        {
            Model = modelId,
            Temperature = temperature,
            MaxTokens = maxTokens,
            Messages = messages,
            EnableThinking = false,
            EnableThinkingCamel = false,
            Thinking = false,
            IncludeReasoning = false,
            ChatTemplateKwargs = ChatTemplateKwargs.Disabled,
            Reasoning = ReasoningOptions.Disabled,
            ResponseFormat = responseFormat,
        };
}

internal sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public object Content { get; init; } = string.Empty;
}

internal abstract class ContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;
}

internal sealed class TextContentPart : ContentPart
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

internal sealed class ImageContentPart : ContentPart
{
    [JsonPropertyName("image_url")]
    public ImageUrl ImageUrl { get; init; } = new();
}

internal sealed class ImageUrl
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
}

internal sealed class ChatTemplateKwargs
{
    public static ChatTemplateKwargs Disabled { get; } = new();

    [JsonPropertyName("enable_thinking")]
    public bool EnableThinking { get; init; }

    [JsonPropertyName("enableThinking")]
    public bool EnableThinkingCamel { get; init; }
}

internal sealed class ReasoningOptions
{
    public static ReasoningOptions Disabled { get; } =
        new() { Enabled = false, Effort = "none", Mode = "off" };

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("effort")]
    public string Effort { get; init; } = "none";

    /// <summary>LM Studio ≥0.4.8: reasoning off/on.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "off";
}

internal sealed class ResponseFormat
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "json_schema";

    [JsonPropertyName("json_schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonSchemaDefinition? JsonSchema { get; init; }
}

internal sealed class JsonSchemaDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "product_analysis";

    [JsonPropertyName("strict")]
    public bool Strict { get; init; } = true;

    [JsonPropertyName("schema")]
    public JsonElement Schema { get; init; }
}

internal sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public IReadOnlyList<ChatChoice>? Choices { get; init; }
}

internal sealed class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatResponseMessage? Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

internal sealed class ChatResponseMessage
{
    [JsonPropertyName("content")]
    public JsonElement Content { get; init; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; init; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }

    /// <summary>
    /// Только content. reasoning_content — thinking; подставлять его нельзя (получим план вместо JSON).
    /// </summary>
    public string? ReadAnswerText() => ReadElement(Content);

    public bool HasReasoningLeak =>
        !string.IsNullOrWhiteSpace(ReasoningContent) || !string.IsNullOrWhiteSpace(Reasoning);

    private static string? ReadElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Array:
                var builder = new StringBuilder();
                foreach (var item in element.EnumerateArray())
                {
                    var piece =
                        item.ValueKind == JsonValueKind.String ? item.GetString()
                        : item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("text", out var text)
                            ? text.GetString()
                        : null;

                    if (!string.IsNullOrWhiteSpace(piece))
                    {
                        if (builder.Length > 0)
                        {
                            builder.Append('\n');
                        }

                        builder.Append(piece);
                    }
                }

                return builder.Length == 0 ? null : builder.ToString();

            case JsonValueKind.Object:
                return element.TryGetProperty("text", out var nested)
                    ? nested.GetString()
                    : element.GetRawText();

            default:
                return null;
        }
    }
}
