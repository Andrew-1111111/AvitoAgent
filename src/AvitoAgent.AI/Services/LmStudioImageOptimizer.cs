using AvitoAgent.Shared.Configuration;
using SkiaSharp;

namespace AvitoAgent.AI.Services;

internal static class LmStudioImageOptimizer
{
    private const int MinUsefulSidePx = 480;

    public static (byte[] Bytes, string MediaType, int Width, int Height) Optimize(
        byte[] input,
        LmStudioOptions options,
        int? maxSidePx = null
    )
    {
        // Всегда JPEG для API. Сторону ограничиваем maxSidePx из Fit (бюджет контекста),
        // иначе Qwen-VL на полном кадре легко превышает n_ctx.
        var side = Math.Clamp(
            maxSidePx ?? options.MaxImageSidePx,
            MinUsefulSidePx,
            2_048
        );
        var quality = options.OptimizeImages
            ? Math.Clamp(options.ImageJpegQuality, 60, 95)
            : 90;

        return EnsureApiCompatible(input, resize: true, maxSidePx: side, quality: quality);
    }

    private static (byte[] Bytes, string MediaType, int Width, int Height) EnsureApiCompatible(
        byte[] input,
        bool resize,
        int? maxSidePx,
        int quality
    )
    {
        using var source = SKBitmap.Decode(input)
            ?? throw new InvalidOperationException("Не удалось декодировать изображение.");

        var width = source.Width;
        var height = source.Height;
        var longest = Math.Max(width, height);

        SKBitmap working = source;
        SKBitmap? resized = null;
        try
        {
            if (resize && maxSidePx is int maxSide)
            {
                if (longest > maxSide)
                {
                    var scale = (float)maxSide / longest;
                    width = Math.Max(1, (int)Math.Round(width * scale));
                    height = Math.Max(1, (int)Math.Round(height * scale));
                    resized = source.Resize(
                        new SKImageInfo(width, height),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                    )
                        ?? throw new InvalidOperationException("Не удалось уменьшить изображение.");
                    working = resized;
                }
                else if (quality >= 90 && IsJpeg(input))
                {
                    return (input, "image/jpeg", width, height);
                }
            }

            using var image = SKImage.FromBitmap(working);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            return (data.ToArray(), "image/jpeg", working.Width, working.Height);
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static bool IsJpeg(byte[] bytes) =>
        bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8;
}
