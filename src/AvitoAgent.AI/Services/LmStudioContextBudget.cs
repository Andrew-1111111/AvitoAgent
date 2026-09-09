using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.AI.Services;

internal static class LmStudioContextBudget
{
    private const int MinImageSidePx = 480;
    private const int VisionPatchPx = 28;
    private const int MaxDescriptionChars = 3_500;
    private const int MinCompletionTokens = 256;
    private const int SafetyTokens = 512;

    /// <summary>
    /// Запасной n_ctx, если LM Studio не отдал context_length.
    /// </summary>
    private const int FallbackContextSize = 32_768;

    private const int MinUsableContext = 2_048;

    /// <summary>
    /// Qwen3.5-VL в LM Studio на полном кадре даёт сильно больше патч-оценки (часто 8k-16k на фото).
    /// </summary>
    private const int ImageTokenSafetyFactor = 3;

    public static int ClampUsagePercent(int percent) => Math.Clamp(percent, 1, 100);

    public static int ResolveContextSize(int queriedContextSize, LmStudioOptions options)
    {
        var full = ResolveFullContextSize(queriedContextSize);
        var percent = ClampUsagePercent(options.ContextUsagePercent);
        var usable = (int)Math.Floor(full * (percent / 100.0));
        return Math.Max(MinUsableContext, usable);
    }

    public static int ResolveFullContextSize(int queriedContextSize) =>
        queriedContextSize > 0 ? queriedContextSize : FallbackContextSize;

    public static int ResolveCompletionTokens(LmStudioOptions options) =>
        options.MaxCompletionTokens > 0 ? options.MaxCompletionTokens : 800;

    public static int CapCompletionTokens(
        int configured,
        int usableContextSize,
        int promptTokens
    ) =>
        Math.Clamp(
            configured,
            MinCompletionTokens,
            Math.Max(MinCompletionTokens, usableContextSize - promptTokens - SafetyTokens)
        );

    public static string TruncateDescription(string? description)
    {
        if (string.IsNullOrEmpty(description) || description.Length <= MaxDescriptionChars)
        {
            return description ?? string.Empty;
        }

        return description[..MaxDescriptionChars] + "…";
    }

    public static int EstimatePromptTokens(
        string systemPrompt,
        string userPrompt,
        int imageCount,
        int maxSidePx
    ) =>
        EstimateTextTokens(systemPrompt)
        + EstimateTextTokens(userPrompt)
        + 400
        + EstimateImageTokens(imageCount, maxSidePx);

    public static (int ImageCount, int MaxSidePx) Fit(
        LmStudioOptions options,
        int availableImages,
        string systemPrompt,
        string userPrompt,
        int contextSize
    )
    {
        // Бюджет - доля loaded context. Фото всегда подгоняем под него (даже при OptimizeImages=false),
        // иначе Qwen-VL легко даёт exceed_context_size_error.
        var context = ResolveContextSize(contextSize, options);
        var configured =
            options.MaxImages <= 0 ? availableImages : Math.Min(options.MaxImages, availableImages);

        var preferredSide = options.OptimizeImages
            ? Math.Clamp(options.MaxImageSidePx, MinImageSidePx, 2_048)
            : Math.Clamp(Math.Max(options.MaxImageSidePx, 1_024), MinImageSidePx, 1_280);

        if (availableImages <= 0 || configured <= 0)
        {
            return (0, preferredSide);
        }

        var completion = Math.Min(
            ResolveCompletionTokens(options),
            Math.Max(MinCompletionTokens, context / 4)
        );
        var budget = Math.Max(2_048, context - completion - SafetyTokens);
        var textTokens = EstimateTextTokens(systemPrompt) + EstimateTextTokens(userPrompt) + 400;

        var imageCount = configured;
        var maxSide = preferredSide;

        while (imageCount > 1 && textTokens + EstimateImageTokens(imageCount, maxSide) > budget)
        {
            imageCount--;
        }

        while (imageCount >= 1 && textTokens + EstimateImageTokens(imageCount, maxSide) > budget)
        {
            if (maxSide <= MinImageSidePx)
            {
                break;
            }

            maxSide = Math.Max(MinImageSidePx, maxSide - 128);
        }

        return (imageCount, maxSide);
    }

    public static (int ImageCount, int MaxSidePx) ShrinkAfterOverflow(
        LmStudioOptions options,
        int imageCount,
        int maxSide,
        ContextOverflowInfo overflow,
        int completionTokens
    )
    {
        var context = ResolveContextSize(overflow.ContextSize, options);
        var prompt = overflow.PromptTokens > 0 ? overflow.PromptTokens : context + 1;
        var target = Math.Max(
            512,
            context - Math.Max(MinCompletionTokens, completionTokens) - SafetyTokens
        );
        var extra = Math.Max(1, prompt - target);

        if (imageCount > 1)
        {
            var tokensPerImage = Math.Max(250, (prompt - 1_500) / imageCount);
            var drop = Math.Max(1, (int)Math.Ceiling(extra / (double)tokensPerImage));
            return (Math.Max(1, imageCount - drop), maxSide);
        }

        if (maxSide > MinImageSidePx)
        {
            return (imageCount, Math.Max(MinImageSidePx, maxSide - 160));
        }

        return (imageCount, maxSide);
    }

    private static int EstimateTextTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, (text.Length + 1) / 2);

    private static int EstimateImageTokens(int imageCount, int maxSidePx)
    {
        if (imageCount <= 0)
        {
            return 0;
        }

        var width = maxSidePx;
        var height = (int)Math.Round(maxSidePx * 0.75);
        var patchTokens =
            (int)(
                Math.Ceiling(width / (double)VisionPatchPx)
                * Math.Ceiling(height / (double)VisionPatchPx)
            ) + 8;
        var perImage = Math.Max(1_024, patchTokens * ImageTokenSafetyFactor);
        return imageCount * perImage;
    }
}
