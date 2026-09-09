using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AvitoAgent.AI.Logging;
using AvitoAgent.AI.Models;
using AvitoAgent.Core;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvitoAgent.AI.Services;

public sealed class LmStudioModelResolver(
    IOptions<LmStudioOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<LmStudioModelResolver> logger
)
{
    private static readonly JsonSerializerOptions LoadJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly LmStudioOptions _options = options.Value;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<LmStudioModelResolver> _logger = logger;
    private LmStudioRuntimeInfo? _cached;

    public void Invalidate() => _cached = null;

    public async Task<LmStudioRuntimeInfo> ResolveRuntimeAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        var client = _httpClientFactory.CreateClient("LmStudio");
        JsonDocument document;
        try
        {
            await using var stream = await client.GetStreamAsync("models", cancellationToken);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw LmStudioErrorMapper.TryMap(ex, _options.BaseUrl)
                ?? LmStudioErrorMapper.NotRunning(_options.BaseUrl, ex);
        }

        using (document)
        {
            var available = EnumerateModelIds(document.RootElement).ToList();
            if (available.Count == 0)
            {
                throw LmStudioErrorMapper.NoModelLoaded();
            }

            var modelId = SelectModelId(available);
            var info = await TryReadNativeContextInfoAsync(client, modelId, cancellationToken);

            if (info is { IsLoaded: false })
            {
                AiLog.ModelNotLoaded(_logger, modelId, info.Value.MaxContextLength ?? 0);
            }

            info = await EnsureLoadedAsync(client, modelId, info, cancellationToken);

            var availableContext =
                info?.AvailableContextLength
                ?? LmStudioContextProbe.ReadContextLength(document.RootElement, modelId)
                ?? 0;

            if (availableContext > LmStudioContextProbe.HugeContextWarnThreshold)
            {
                AiLog.ModelContextHuge(_logger, modelId, availableContext);
            }

            var usable = LmStudioContextBudget.ResolveContextSize(availableContext, _options);
            if (availableContext > 0)
            {
                AiLog.ModelContextBudget(
                    _logger,
                    modelId,
                    availableContext,
                    _options.ContextUsagePercent,
                    usable
                );
            }
            else
            {
                AiLog.ModelContextUnknown(
                    _logger,
                    modelId,
                    LmStudioContextBudget.ResolveFullContextSize(0)
                );
            }

            _cached = new LmStudioRuntimeInfo(modelId, availableContext);
            return _cached.Value;
        }
    }

    private async Task<ModelContextInfo?> EnsureLoadedAsync(
        HttpClient client,
        string modelId,
        ModelContextInfo? info,
        CancellationToken cancellationToken
    )
    {
        if (!_options.AutoLoadModel)
        {
            return info;
        }

        var wantContext = Math.Max(0, _options.LoadContextLength);
        var loadedCtx = info?.LoadedContextLength ?? 0;
        var needsReload =
            info is not { IsLoaded: true }
            || (wantContext > 0 && loadedCtx > 0 && loadedCtx < wantContext);

        if (!needsReload)
        {
            return info;
        }

        if (info is { IsLoaded: true } && loadedCtx > 0 && wantContext > loadedCtx)
        {
            AiLog.ModelContextTooSmall(_logger, modelId, loadedCtx, wantContext);
            await TryUnloadModelAsync(client, modelId, cancellationToken);
        }

        AiLog.ModelLoading(_logger, modelId, wantContext);

        try
        {
            var loaded = await LoadModelAsync(client, modelId, wantContext, cancellationToken);
            AiLog.ModelLoadSucceeded(
                _logger,
                modelId,
                loaded.LoadTimeSeconds,
                loaded.ContextLength > 0 ? loaded.ContextLength : wantContext
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var reason = ex.Message;
            AiLog.ModelLoadFailed(_logger, modelId, reason);
            throw LmStudioErrorMapper.TryMap(ex, _options.BaseUrl)
                ?? new LmStudioUnavailableException(
                    $"Не удалось загрузить модель {modelId} в LM Studio: {reason}",
                    ex
                );
        }

        // После load перечитываем native API - там появляется loaded_context_length.
        return await TryReadNativeContextInfoAsync(client, modelId, cancellationToken) ?? info;
    }

    private async Task TryUnloadModelAsync(
        HttpClient client,
        string modelId,
        CancellationToken cancellationToken
    )
    {
        if (!TryBuildNativeUnloadUri(client.BaseAddress, out var uri))
        {
            return;
        }

        try
        {
            using var response = await client.PostAsJsonAsync(
                uri,
                new { instance_id = modelId },
                LoadJson,
                cancellationToken
            );
            _ = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "LM Studio: unload {ModelId} не удался", modelId);
            }
        }
    }

    private static async Task<ModelLoadResult> LoadModelAsync(
        HttpClient client,
        string modelId,
        int contextLength,
        CancellationToken cancellationToken
    )
    {
        if (!TryBuildNativeLoadUri(client.BaseAddress, out var uri))
        {
            throw new InvalidOperationException(
                "Не удалось построить URL /api/v1/models/load из LmStudio:BaseUrl."
            );
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = modelId,
            ["flash_attention"] = true,
            ["echo_load_config"] = true,
        };
        if (contextLength > 0)
        {
            body["context_length"] = contextLength;
        }

        using var response = await client.PostAsJsonAsync(uri, body, LoadJson, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw LmStudioErrorMapper.FromHttpError((int)response.StatusCode, responseText);
        }

        return ParseLoadResult(responseText, modelId, contextLength);
    }

    private static ModelLoadResult ParseLoadResult(
        string responseText,
        string modelId,
        int requestedContext
    )
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var seconds =
                root.TryGetProperty("load_time_seconds", out var t) && t.TryGetDouble(out var sec)
                    ? sec
                    : 0;
            var context = requestedContext;
            if (
                root.TryGetProperty("load_config", out var cfg)
                && cfg.ValueKind == JsonValueKind.Object
                && cfg.TryGetProperty("context_length", out var cl)
                && cl.TryGetInt32(out var loadedCtx)
            )
            {
                context = loadedCtx;
            }

            return new ModelLoadResult(modelId, seconds, context);
        }
        catch (JsonException)
        {
            return new ModelLoadResult(modelId, 0, requestedContext);
        }
    }

    /// <summary>
    /// OpenAI <c>/v1/models</c> не отдаёт context_length.
    /// Native <c>/api/v0/models</c> - <c>loaded_context_length</c> / <c>max_context_length</c>.
    /// </summary>
    private async Task<ModelContextInfo?> TryReadNativeContextInfoAsync(
        HttpClient client,
        string modelId,
        CancellationToken cancellationToken
    )
    {
        if (!TryBuildNativeModelsUri(client.BaseAddress, out var uri))
        {
            return null;
        }

        try
        {
            await using var stream = await client.GetStreamAsync(uri, cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken
            );
            return LmStudioContextProbe.ReadContextInfo(document.RootElement, modelId);
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "LM Studio: не удалось прочитать {Uri} для context_length", uri);
            }

            return null;
        }
    }

    internal static bool TryBuildNativeModelsUri(Uri? baseAddress, out Uri uri)
    {
        uri = null!;
        if (baseAddress is null)
        {
            return false;
        }

        // http://localhost:1234/v1/ → http://localhost:1234/api/v0/models
        var builder = new UriBuilder(baseAddress)
        {
            Path = "/api/v0/models",
            Query = string.Empty,
            Fragment = string.Empty,
        };
        uri = builder.Uri;
        return true;
    }

    internal static bool TryBuildNativeLoadUri(Uri? baseAddress, out Uri uri)
    {
        uri = null!;
        if (baseAddress is null)
        {
            return false;
        }

        // http://localhost:1234/v1/ → http://localhost:1234/api/v1/models/load
        var builder = new UriBuilder(baseAddress)
        {
            Path = "/api/v1/models/load",
            Query = string.Empty,
            Fragment = string.Empty,
        };
        uri = builder.Uri;
        return true;
    }

    internal static bool TryBuildNativeUnloadUri(Uri? baseAddress, out Uri uri)
    {
        uri = null!;
        if (baseAddress is null)
        {
            return false;
        }

        var builder = new UriBuilder(baseAddress)
        {
            Path = "/api/v1/models/unload",
            Query = string.Empty,
            Fragment = string.Empty,
        };
        uri = builder.Uri;
        return true;
    }

    private string SelectModelId(List<string> available)
    {
        if (!string.IsNullOrWhiteSpace(_options.Model))
        {
            var exact = available.FirstOrDefault(id =>
                id.Equals(_options.Model, StringComparison.OrdinalIgnoreCase)
            );

            if (exact is not null)
            {
                AiLog.ModelSelected(_logger, exact);
                return exact;
            }

            var partial = available.FirstOrDefault(id =>
                id.Contains(_options.Model, StringComparison.OrdinalIgnoreCase)
            );

            if (partial is not null)
            {
                AiLog.ModelFallback(_logger, _options.Model, partial);
                return partial;
            }

            if (!_options.AutoSelectModel)
            {
                throw LmStudioErrorMapper.ModelNotFound(_options.Model, available);
            }

            AiLog.ModelNotFoundUsingFirst(_logger, _options.Model, available[0]);
        }

        AiLog.ModelSelected(_logger, available[0]);
        return available[0];
    }

    private static IEnumerable<string> EnumerateModelIds(JsonElement root)
    {
        if (
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var item in data.EnumerateArray())
            {
                if (
                    item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.String
                )
                {
                    var value = id.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
            }

            yield break;
        }

        // LM Studio иногда отдаёт models вместо data.
        if (
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("models", out var models)
            && models.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var item in models.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var name in new[] { "id", "key", "name" })
                {
                    if (
                        item.TryGetProperty(name, out var id)
                        && id.ValueKind == JsonValueKind.String
                    )
                    {
                        var value = id.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            yield return value;
                            break;
                        }
                    }
                }
            }
        }
    }

    private readonly record struct ModelLoadResult(
        string ModelId,
        double LoadTimeSeconds,
        int ContextLength
    );
}
