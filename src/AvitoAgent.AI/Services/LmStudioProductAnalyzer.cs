using System.Net.Http.Json;
using System.Text.Json;
using AvitoAgent.AI.Logging;
using AvitoAgent.AI.Models;
using AvitoAgent.Core.Interfaces;
using AvitoAgent.Core.Models;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvitoAgent.AI.Services;

public sealed class LmStudioProductAnalyzer(
    IHttpClientFactory httpClientFactory,
    PromptProvider promptProvider,
    LmStudioModelResolver modelResolver,
    IOptions<LmStudioOptions> options,
    ILogger<LmStudioProductAnalyzer> logger
) : IProductAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly PromptProvider _promptProvider = promptProvider;
    private readonly LmStudioModelResolver _modelResolver = modelResolver;
    private readonly LmStudioOptions _options = options.Value;
    private readonly ILogger<LmStudioProductAnalyzer> _logger = logger;

    public async Task EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Старт: проверка LM Studio + автозагрузка модели + чтение контекста.
        _ = await _modelResolver.ResolveRuntimeAsync(cancellationToken);
    }

    public async Task<ProductAnalysis> AnalyzeAsync(
        Listing listing,
        SearchCriteria criteria,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var systemPrompt = _options.UseExternalSystemPrompt
                ? string.Empty
                : _promptProvider.GetAuthenticityPrompt();
            var userPrompt = QwenThinking.PrefixUserText(BuildUserPrompt(listing, criteria));
            var runtime = await _modelResolver.ResolveRuntimeAsync(cancellationToken);
            var (imageParts, imageMaxSide) = await LoadImagesAsync(
                listing,
                systemPrompt,
                userPrompt,
                runtime.ContextLength,
                cancellationToken
            );
            var modelId = runtime.ModelId;

            var messages = new List<ChatMessage>();

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new ChatMessage { Role = "system", Content = systemPrompt });
            }

            messages.Add(
                new ChatMessage { Role = "user", Content = BuildUserContent(userPrompt, imageParts) }
            );

            // json_schema + <think> prefill = 400 «empty grammar stack».
            // Для Qwen3.5: без schema, с prefill закрытого think и стартом JSON.
            var useSchema = _options.UseJsonSchemaResponse;
            if (!useSchema)
            {
                messages.Add(
                    new ChatMessage
                    {
                        Role = "assistant",
                        Content = QwenThinking.BuildAssistantPrefill(startJsonObject: true),
                    }
                );
            }

            var usableContext = LmStudioContextBudget.ResolveContextSize(
                runtime.ContextLength,
                _options
            );
            var promptEstimate = LmStudioContextBudget.EstimatePromptTokens(
                systemPrompt,
                userPrompt,
                imageParts.Count,
                imageMaxSide
            );
            var maxTokens = LmStudioContextBudget.CapCompletionTokens(
                LmStudioContextBudget.ResolveCompletionTokens(_options),
                usableContext,
                promptEstimate
            );

            var request = ChatCompletionRequest.CreateWithoutThinking(
                modelId,
                messages,
                _options.Temperature,
                maxTokens,
                useSchema ? ProductAnalysisResponseFormat.Create() : null
            );

            AiLog.NoThinkEnabled(_logger, listing.Id);

            var client = _httpClientFactory.CreateClient("LmStudio");
            var perImageSeconds = 90;
            var timeoutSeconds = Math.Max(
                _options.RequestTimeoutSeconds,
                Math.Max(1, imageParts.Count) * perImageSeconds
            );
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            AiLog.AnalyzingListing(_logger, listing.Id, modelId, imageParts.Count);

            using var response = await PostCompletionAsync(
                client,
                request,
                listing.Id,
                cancellationToken
            );

            var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(
                JsonOptions,
                cancellationToken
            );

            var message = completion?.Choices?.FirstOrDefault()?.Message;
            if (message?.HasReasoningLeak == true)
            {
                AiLog.ThinkingStillActive(_logger, listing.Id);
            }

            var content = message?.ReadAnswerText();
            if (
                !string.IsNullOrWhiteSpace(content)
                && !content.TrimStart().StartsWith('{')
                && content.Contains("authenticityScore", StringComparison.OrdinalIgnoreCase)
            )
            {
                content = "{" + content;
            }

            content = QwenThinking.StripThinkBlocks(content ?? string.Empty);

            if (string.IsNullOrWhiteSpace(content))
            {
                if (message?.HasReasoningLeak == true)
                {
                    throw new InvalidOperationException(
                        "LM Studio thinking всё ещё включён: reasoning заполнен, content пуст. "
                            + "В LM Studio у модели выключите Thinking (или Enable Thinking) и перезагрузите модель."
                    );
                }

                throw new InvalidOperationException("LM Studio вернул пустой ответ.");
            }

            LlmAnalysisResult parsed;
            try
            {
                parsed = LlmResponseParser.Parse(content);
            }
            catch (InvalidOperationException)
            {
                AiLog.InvalidJsonResponse(_logger, listing.Id, PreviewForLog(content));
                throw;
            }

            AiLog.AnalysisCompleted(_logger, listing.Id, parsed.IsAuthentic, parsed.Score);

            return new ProductAnalysis
            {
                Score = parsed.IsRelevant && parsed.Score < 70 ? Math.Max(parsed.Score, 70) : parsed.Score,
                IsRelevant = parsed.IsRelevant,
                IsAuthentic = parsed.IsAuthentic,
                AuthenticityScore = parsed.AuthenticityScore,
                Brand = parsed.Brand ?? string.Empty,
                Model = parsed.Model ?? string.Empty,
                Category = parsed.Category ?? string.Empty,
                Condition = parsed.Condition ?? string.Empty,
                Reason = parsed.Reason ?? string.Empty,
                DetectedFeatures = parsed.DetectedFeatures ?? [],
                CounterfeitIndicators = parsed.CounterfeitIndicators ?? [],
                AnalyzerModel = modelId,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw LmStudioErrorMapper.TryMapAnalysisError(ex, _options.BaseUrl) ?? ex;
        }
    }

    private async Task<HttpResponseMessage> PostCompletionAsync(
        HttpClient client,
        ChatCompletionRequest request,
        string listingId,
        CancellationToken cancellationToken
    )
    {
        var response = await client.PostAsJsonAsync("chat/completions", request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = (int)response.StatusCode;

        // Schema / grammar конфликт (часто из-за <think> + json_schema) - без schema + think-prefill.
        if (
            status == 400
            && request.ResponseFormat is not null
            && (
                errorBody.Contains("response_format", StringComparison.OrdinalIgnoreCase)
                || errorBody.Contains("grammar", StringComparison.OrdinalIgnoreCase)
                || errorBody.Contains("samplers", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            response.Dispose();
            AiLog.RequestFailed(_logger, listingId, status, errorBody);

            var fallbackMessages = request.Messages
                .Where(static m =>
                    !string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            fallbackMessages.Add(
                new ChatMessage
                {
                    Role = "assistant",
                    Content = QwenThinking.BuildAssistantPrefill(startJsonObject: true),
                }
            );

            var fallback = ChatCompletionRequest.CreateWithoutThinking(
                request.Model,
                fallbackMessages,
                request.Temperature,
                request.MaxTokens,
                responseFormat: null
            );

            response = await client.PostAsJsonAsync(
                "chat/completions",
                fallback,
                cancellationToken
            );
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            status = (int)response.StatusCode;
        }

        response.Dispose();
        AiLog.RequestFailed(_logger, listingId, status, errorBody);
        _modelResolver.Invalidate();
        throw LmStudioErrorMapper.FromHttpError(status, errorBody);
    }

    private static List<object> BuildUserContent(
        string userPrompt,
        IReadOnlyList<ImageContentPart> imageParts
    )
    {
        var parts = new List<object>
        {
            new TextContentPart { Type = "text", Text = userPrompt },
        };

        parts.AddRange(imageParts);

        // Инструкция после фото - vision-модели чаще слушаются хвост запроса.
        parts.Add(
            new TextContentPart
            {
                Type = "text",
                Text =
                    "/no_think\nОтветь ТОЛЬКО одним JSON-объектом: соответствует ли объявление searchCriteria.keywords "
                        + "(тип, бренд/модель и уточнения из запроса). Синонимы того же типа учитывай. "
                        + "Текст важнее спорного фото. Без английского. Первый символ: {",
            }
        );

        return parts;
    }

    private static string BuildUserPrompt(Listing listing, SearchCriteria criteria)
    {
        var payload = new
        {
            searchCriteria = criteria,
            listing = new
            {
                listing.Id,
                listing.Title,
                Description = LmStudioContextBudget.TruncateDescription(listing.Description),
                listing.Price,
                listing.Currency,
                listing.Location,
                listing.SellerName,
                listing.Url,
                imageCount = listing.Images.Count > 0 ? listing.Images.Count : listing.ImageUrls.Count,
            },
        };

        return QwenThinking.PrefixUserText(
            "Данные объявления. Реши, соответствует ли объявление searchCriteria "
                + "(тип товара, бренд/модель, исключения) - это твоя задача; ответь JSON:\n"
                + JsonSerializer.Serialize(payload, JsonOptions)
        );
    }

    private async Task<(IReadOnlyList<ImageContentPart> Parts, int MaxSidePx)> LoadImagesAsync(
        Listing listing,
        string systemPrompt,
        string userPrompt,
        int contextLength,
        CancellationToken cancellationToken
    )
    {
        var browserPhotos = listing
            .Images.Where(static photo => photo.Content.Length > 0)
            .OrderBy(static photo => photo.SortOrder)
            .ToList();

        // При MaxImages=1 берём самый тяжёлый кадр (обычно выше разрешение), не обязательно первый по порядку.
        if (_options.MaxImages == 1 && browserPhotos.Count > 1)
        {
            browserPhotos =
            [
                browserPhotos.OrderByDescending(static photo => photo.Content.Length).First(),
            ];
        }

        var available =
            browserPhotos.Count > 0 ? browserPhotos.Count : listing.ImageUrls.Count;
        if (available == 0)
        {
            return ([], _options.MaxImageSidePx);
        }

        var (imageCount, maxSide) = LmStudioContextBudget.Fit(
            _options,
            available,
            systemPrompt,
            userPrompt,
            contextLength
        );

        var parts = new List<ImageContentPart>();
        var totalPayloadBytes = 0;
        var outWidth = 0;
        var outHeight = 0;

        if (browserPhotos.Count > 0)
        {
            foreach (var photo in browserPhotos.Take(imageCount))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddImagePart(photo.Content, maxSide, parts, ref totalPayloadBytes, ref outWidth, ref outHeight);
            }
        }
        else
        {
            var client = _httpClientFactory.CreateClient("Avito");
            foreach (var url in listing.ImageUrls.Take(imageCount))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var rawBytes = await client.GetByteArrayAsync(url, cancellationToken);
                    AddImagePart(rawBytes, maxSide, parts, ref totalPayloadBytes, ref outWidth, ref outHeight);
                }
                catch (Exception ex)
                {
                    AiLog.ImageDownloadFailed(_logger, url, ex);
                }
            }
        }

        var fullContext = LmStudioContextBudget.ResolveFullContextSize(contextLength);
        var usable = LmStudioContextBudget.ResolveContextSize(contextLength, _options);
        var percent = LmStudioContextBudget.ClampUsagePercent(_options.ContextUsagePercent);
        var sizeLabel =
            outWidth > 0 && outHeight > 0
                ? $"{outWidth}×{outHeight} JPEG (≤{maxSide}px под бюджет)"
                : $"сторона ≤{maxSide}px";
        AiLog.ImagesPrepared(
            _logger,
            parts.Count,
            available,
            totalPayloadBytes / 1024,
            sizeLabel,
            usable,
            fullContext,
            percent
        );

        return (parts, maxSide);
    }

    private void AddImagePart(
        byte[] rawBytes,
        int maxSide,
        List<ImageContentPart> parts,
        ref int totalPayloadBytes,
        ref int outWidth,
        ref int outHeight
    )
    {
        var (bytes, mediaType, width, height) = LmStudioImageOptimizer.Optimize(
            rawBytes,
            _options,
            maxSide
        );
        totalPayloadBytes += bytes.Length;
        if (width > 0 && height > 0)
        {
            outWidth = width;
            outHeight = height;
        }
        parts.Add(
            new ImageContentPart
            {
                Type = "image_url",
                ImageUrl = new ImageUrl
                {
                    Url = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}",
                },
            }
        );
    }

    private static string PreviewForLog(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 280 ? flat : flat[..280] + "…";
    }
}
