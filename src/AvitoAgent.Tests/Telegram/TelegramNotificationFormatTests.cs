using AvitoAgent.Core.Models;
using AvitoAgent.Telegram.Services;

namespace AvitoAgent.Tests.Telegram;

public sealed class TelegramNotificationFormatTests
{
    [Fact]
    public void BuildMessage_includes_title_price_address_url()
    {
        var listing = new Listing
        {
            Title = " Генератор ",
            Price = 15_000,
            Currency = "RUB",
            Location = " Москва ",
            Description = "описание",
            Url = "https://www.avito.ru/item/1",
        };

        var text = TelegramNotificationService.BuildMessage(listing);
        Assert.Contains("Генератор", text);
        Assert.Contains("руб.", text);
        Assert.Contains("Москва", text);
        Assert.Contains("описание", text);
        Assert.Contains("https://www.avito.ru/item/1", text);
    }

    [Fact]
    public void FormatPrice_and_address_fallbacks()
    {
        Assert.Equal(
            "не указана",
            TelegramNotificationService.FormatPrice(new Listing { Price = null })
        );
        Assert.Contains(
            "USD",
            TelegramNotificationService.FormatPrice(new Listing { Price = 10, Currency = "USD" })
        );
        Assert.Equal("не указан", TelegramNotificationService.FormatAddress(new Listing()));
    }

    [Fact]
    public void TrimDescription_truncates_at_1500()
    {
        var longText = new string('x', 1600);
        var trimmed = TelegramNotificationService.TrimDescription(longText);
        Assert.True(trimmed.Length < longText.Length);
        Assert.EndsWith("…", trimmed);
    }

    [Fact]
    public void TruncateForCaption_keeps_url()
    {
        var url = "https://www.avito.ru/item/long";
        var body = new string('a', 2000);
        var caption = TelegramNotificationService.TruncateForCaption(body + "\n\n" + url, url);
        Assert.True(caption.Length <= 1024);
        Assert.EndsWith(url, caption);
    }

    [Fact]
    public void SplitText_prefers_newlines()
    {
        var text = string.Join('\n', Enumerable.Range(0, 50).Select(i => $"line-{i}-" + new string('x', 80)));
        var parts = TelegramNotificationService.SplitText(text, 400);
        Assert.True(parts.Length > 1);
        Assert.All(parts, part => Assert.True(part.Length <= 400));
    }

    [Fact]
    public void ResolveContentType_and_file_name()
    {
        Assert.Equal("image/jpeg", TelegramNotificationService.ResolveContentType(""));
        Assert.Equal("image/png", TelegramNotificationService.ResolveContentType("image/png"));
        Assert.Equal("image/jpeg", TelegramNotificationService.ResolveContentType("application/octet-stream"));

        var photo = new ListingPhoto { ContentType = "image/webp", Content = [1] };
        Assert.Equal("photo.webp", TelegramNotificationService.ResolveFileName(photo, "photo"));
    }
}
