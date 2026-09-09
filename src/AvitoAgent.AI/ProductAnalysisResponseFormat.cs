using System.Text.Json;
using AvitoAgent.AI.Models;

namespace AvitoAgent.AI;

internal static class ProductAnalysisResponseFormat
{
    // LM Studio: type-массивы и strict часто ломают json_schema - упрощённая схема.
    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "isAuthentic": { "type": ["boolean", "null"] },
                "authenticityScore": { "type": "integer" },
                "brand": { "type": ["string", "null"] },
                "model": { "type": ["string", "null"] },
                "category": { "type": ["string", "null"] },
                "condition": { "type": ["string", "null"] },
                "score": { "type": "integer" },
                "isRelevant": { "type": "boolean" },
                "reason": { "type": "string" },
                "detectedFeatures": { "type": "array", "items": { "type": "string" } },
                "counterfeitIndicators": { "type": "array", "items": { "type": "string" } },
                "confidence": { "type": "number" }
              },
              "required": [
                "isAuthentic",
                "authenticityScore",
                "score",
                "isRelevant",
                "reason",
                "detectedFeatures",
                "counterfeitIndicators",
                "confidence"
              ]
            }
            """
        )
        .RootElement;

    public static ResponseFormat Create() =>
        new()
        {
            Type = "json_schema",
            JsonSchema = new JsonSchemaDefinition
            {
                Name = "product_analysis",
                Strict = false,
                Schema = Schema,
            },
        };
}
