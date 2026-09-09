using System.Text.Json;

namespace AvitoAgent.AI.Services;

internal static class LmStudioContextProbe
{
    /// <summary>
    /// Порог для предупреждения: выше - в LM Studio лучше уменьшить n_ctx при загрузке.
    /// На расчёт ContextUsagePercent не влияет.
    /// </summary>
    public const int HugeContextWarnThreshold = 32_768;

    public static ModelContextInfo? ReadContextInfo(JsonElement root, string modelId)
    {
        JsonElement? matched = null;
        JsonElement? anyLoaded = null;

        foreach (var entry in EnumerateModels(root))
        {
            if (Matches(entry, modelId))
            {
                matched = entry;
            }

            if (IsLoaded(entry) && anyLoaded is null)
            {
                anyLoaded = entry;
            }
        }

        var model = matched ?? anyLoaded;
        if (model is null)
        {
            return null;
        }

        var element = model.Value;
        var loaded = IsLoaded(element);
        var loadedCtx = ReadLoadedContext(element) ?? 0;
        var maxCtx = ReadMaxContext(element) ?? 0;

        // Доступный контекст модели: фактически выделенный, иначе архитектурный max из /api/v0.
        var available = loadedCtx > 0 ? loadedCtx : maxCtx;

        return new ModelContextInfo(
            ModelId: ReadId(element) ?? modelId,
            IsLoaded: loaded,
            LoadedContextLength: loadedCtx > 0 ? loadedCtx : null,
            MaxContextLength: maxCtx > 0 ? maxCtx : null,
            AvailableContextLength: available > 0 ? available : null
        );
    }

    public static int? ReadContextLength(JsonElement root, string modelId) =>
        ReadContextInfo(root, modelId)?.AvailableContextLength;

    private static bool IsLoaded(JsonElement model)
    {
        if (TryGet(model, "state", out var state) && state.ValueKind == JsonValueKind.String)
        {
            return string.Equals(state.GetString(), "loaded", StringComparison.OrdinalIgnoreCase);
        }

        if (
            TryGet(model, "loaded_instances", out var instances)
            && instances.ValueKind == JsonValueKind.Array
        )
        {
            return instances.GetArrayLength() > 0;
        }

        return ReadLoadedContext(model) is > 0;
    }

    private static string? ReadId(JsonElement model)
    {
        foreach (var name in new[] { "id", "key", "name" })
        {
            if (TryGet(model, name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateModels(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var name in new[] { "models", "data" })
        {
            if (TryGet(root, name, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        yield return item;
                    }
                }

                yield break;
            }
        }

        yield return root;
    }

    private static bool Matches(JsonElement model, string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        foreach (var name in new[] { "id", "key", "display_name", "name" })
        {
            if (!TryGet(model, name, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (
                string.Equals(text, modelId, StringComparison.OrdinalIgnoreCase)
                || text.Contains(modelId, StringComparison.OrdinalIgnoreCase)
                || modelId.Contains(text, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static int? ReadLoadedContext(JsonElement model)
    {
        var direct = ReadPositiveInt(model, "loaded_context_length");
        if (direct > 0)
        {
            return direct;
        }

        if (
            !TryGet(model, "loaded_instances", out var instances)
            || instances.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        foreach (var instance in instances.EnumerateArray())
        {
            if (instance.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var fromInstance = ReadPositiveInt(instance, "context_length", "n_ctx");
            if (fromInstance > 0)
            {
                return fromInstance;
            }

            if (
                TryGet(instance, "config", out var config)
                && config.ValueKind == JsonValueKind.Object
            )
            {
                var fromConfig = ReadPositiveInt(config, "context_length", "n_ctx");
                if (fromConfig > 0)
                {
                    return fromConfig;
                }
            }
        }

        return null;
    }

    private static int? ReadMaxContext(JsonElement model) =>
        ReadPositiveInt(model, "max_context_length", "max_model_len");

    private static int? ReadPositiveInt(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(obj, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt32(out var integer) && integer > 0)
                {
                    return integer;
                }

                if (value.TryGetDouble(out var number) && number > 0)
                {
                    return (int)number;
                }
            }

            if (
                value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), out var parsed)
                && parsed > 0
            )
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

internal readonly record struct ModelContextInfo(
    string ModelId,
    bool IsLoaded,
    int? LoadedContextLength,
    int? MaxContextLength,
    int? AvailableContextLength
);
