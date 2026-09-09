using AvitoAgent.Core.Models;
using AvitoAgent.Telegram.Services;

namespace AvitoAgent.Tests.Telegram;

public sealed class TelegramBotParseTests
{
    [Theory]
    [InlineData("1000-5000", 1000, 5000)]
    [InlineData("от 2000 до 8000", 2000, 8000)]
    [InlineData("5000", 5000, 0)]
    [InlineData("до 3000", 0, 3000)]
    public void TryParsePrice_ok(string text, int min, int max)
    {
        Assert.True(TelegramBotService.TryParsePrice(text, out var a, out var b, out var error));
        Assert.Empty(error);
        Assert.Equal(min, a);
        Assert.Equal(max, b);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("нет")]
    [InlineData("любая")]
    [InlineData("без границ")]
    public void TryParsePrice_clear(string text)
    {
        Assert.True(TelegramBotService.TryParsePrice(text, out var min, out var max, out _));
        Assert.Equal(0, min);
        Assert.Equal(0, max);
    }

    [Fact]
    public void TryParsePrice_invalid()
    {
        Assert.False(TelegramBotService.TryParsePrice("abc", out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("15 мин", 15)]
    [InlineData("20 минут", 20)]
    public void TryParseIntervalMinutes_ok(string text, int expected)
    {
        Assert.True(TelegramBotService.TryParseIntervalMinutes(text, out var minutes, out _));
        Assert.Equal(expected, minutes);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    public void TryParseIntervalMinutes_invalid(string text)
    {
        Assert.False(TelegramBotService.TryParseIntervalMinutes(text, out _, out var error));
        Assert.Contains("минут", error);
    }

    [Theory]
    [InlineData("23-7", 23, 7)]
    [InlineData("1 8", 1, 8)]
    [InlineData("22:6", 22, 6)]
    public void TryParseSleepHours_ok(string text, int from, int to)
    {
        Assert.True(TelegramBotService.TryParseSleepHours(text, out var a, out var b, out _));
        Assert.Equal(from, a);
        Assert.Equal(to, b);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("выкл")]
    [InlineData("off")]
    public void TryParseSleepHours_clear(string text)
    {
        Assert.True(TelegramBotService.TryParseSleepHours(text, out var from, out var to, out _));
        Assert.Null(from);
        Assert.Null(to);
    }

    [Theory]
    [InlineData("12-12")]
    [InlineData("25-1")]
    [InlineData("abc")]
    public void TryParseSleepHours_invalid(string text)
    {
        Assert.False(TelegramBotService.TryParseSleepHours(text, out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void SplitPhrases_and_clear_token()
    {
        Assert.Equal(["a", "b"], TelegramBotService.SplitPhrases("a, b; -"));
        Assert.True(TelegramBotService.IsClearListToken("-"));
        Assert.True(TelegramBotService.IsClearListToken("нет"));
        Assert.False(TelegramBotService.IsClearListToken("генератор"));
    }

    [Fact]
    public void Format_helpers()
    {
        Assert.Equal("Сон выключен", TelegramBotService.FormatSleepSaved(null, null));
        Assert.Contains("23:00-07:00", TelegramBotService.FormatSleepSaved(23, 7));

        var any = new ParseSettings { MinPrice = 0, MaxPrice = 0 };
        Assert.Equal("любая", TelegramBotService.FormatPrice(any));
        Assert.Equal("от 100 ₽", TelegramBotService.FormatPrice(any with { MinPrice = 100 }));
        Assert.Equal("до 200 ₽", TelegramBotService.FormatPrice(any with { MaxPrice = 200 }));
        Assert.Equal(
            "100-200 ₽",
            TelegramBotService.FormatPrice(any with { MinPrice = 100, MaxPrice = 200 })
        );

        Assert.Equal("-", TelegramBotService.JoinOrDash([]));
        Assert.Equal("a, b", TelegramBotService.JoinOrDash(["a", "b"]));

        Assert.Equal(
            "выкл",
            TelegramBotService.FormatSleep(new ParseSettings { SleepFromHour = null, SleepToHour = null })
        );
    }
}
