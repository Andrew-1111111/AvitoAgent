using AvitoAgent.Avito.Parsing;

namespace AvitoAgent.Tests.Avito;

public sealed class AvitoPublishedAtParserTests
{
    private static readonly DateTime NowUtc = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_empty_returns_null(string? raw)
    {
        Assert.Null(AvitoPublishedAtParser.Parse(raw, NowUtc));
    }

    [Fact]
    public void Parse_iso()
    {
        var result = AvitoPublishedAtParser.Parse("2026-01-15T10:30:00Z", NowUtc);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void Parse_just_now()
    {
        Assert.Equal(NowUtc, AvitoPublishedAtParser.Parse("только что", NowUtc));
        Assert.Equal(NowUtc, AvitoPublishedAtParser.Parse("сейчас", NowUtc));
    }

    [Fact]
    public void Parse_yesterday()
    {
        var result = AvitoPublishedAtParser.Parse("вчера", NowUtc);
        Assert.NotNull(result);
        // MSK yesterday noon -> UTC
        Assert.True(result!.Value < NowUtc);
    }

    [Fact]
    public void Parse_minutes_ago()
    {
        var result = AvitoPublishedAtParser.Parse("5 минут назад", NowUtc);
        Assert.NotNull(result);
        Assert.Equal(NowUtc.AddMinutes(-5), result!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Parse_named_date()
    {
        var result = AvitoPublishedAtParser.Parse("15 января", NowUtc);
        Assert.NotNull(result);
        Assert.Equal(2026, result!.Value.Year);
        Assert.Equal(1, TimeZoneInfo.ConvertTimeFromUtc(result.Value, Moscow()).Month);
        Assert.Equal(15, TimeZoneInfo.ConvertTimeFromUtc(result.Value, Moscow()).Day);
    }

    [Theory]
    [InlineData("сегодня в 14:30")]
    [InlineData("вчера в 9:05")]
    [InlineData("час назад")]
    [InlineData("3 часа назад")]
    [InlineData("2 дня назад")]
    [InlineData("2 дн назад")]
    [InlineData("1 неделю назад")]
    [InlineData("Размещено 5 минут назад")]
    public void Parse_relative_forms(string raw)
    {
        Assert.NotNull(AvitoPublishedAtParser.Parse(raw, NowUtc));
    }

    [Fact]
    public void Parse_today_without_time_is_now_moscow()
    {
        var result = AvitoPublishedAtParser.Parse("сегодня", NowUtc);
        Assert.Equal(NowUtc, result);
    }

    [Fact]
    public void Parse_unparseable_returns_null()
    {
        Assert.Null(AvitoPublishedAtParser.Parse("когда-нибудь потом", NowUtc));
    }

    private static TimeZoneInfo Moscow() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
}

public sealed class AvitoImageUrlTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("data:image/png;base64,xxx")]
    [InlineData("https://www.avito.ru/static/avatar.png")]
    public void Normalize_rejects_junk(string? raw)
    {
        Assert.Null(AvitoImageUrl.NormalizeAndUpgrade(raw));
    }

    [Fact]
    public void Normalize_upgrades_size_and_protocol_relative()
    {
        var url = AvitoImageUrl.NormalizeAndUpgrade("//www.avito.st/image/abc/240x180/photo.jpg?x=1");
        Assert.NotNull(url);
        Assert.StartsWith("https://", url);
        Assert.Contains(AvitoImageUrl.FullSize, url!);
        Assert.DoesNotContain("?", url);
    }

    [Fact]
    public void Normalize_keeps_signed_image_path()
    {
        var raw = "https://www.avito.st/image/1/1.abc.def/240x180?cqp=sig";
        var url = AvitoImageUrl.NormalizeAndUpgrade(raw);
        Assert.Equal(raw, url);
    }

    [Fact]
    public void IsPreview_by_size_and_path()
    {
        Assert.True(AvitoImageUrl.IsPreview("https://x/240x180/img.jpg"));
        Assert.False(AvitoImageUrl.IsPreview("https://x/640x480/img.jpg"));
        Assert.True(AvitoImageUrl.IsPreview("https://x/io/preview/img.jpg"));
        Assert.True(AvitoImageUrl.IsPreview(""));
    }

    [Fact]
    public void WidthHint_and_Quality()
    {
        Assert.Equal(1280, AvitoImageUrl.WidthHint("https://cdn/1280x960/a.jpg"));
        Assert.Null(AvitoImageUrl.WidthHint("https://cdn/a.jpg"));
        Assert.True(AvitoImageUrl.Quality("https://cdn/1280x960/a.jpg") > AvitoImageUrl.Quality("https://cdn/240x180/a.jpg"));
    }

    [Fact]
    public void Identity_stable_for_resized_variants()
    {
        var a = AvitoImageUrl.Identity("https://cdn/image/1/1.abc.def/240x180");
        var b = AvitoImageUrl.Identity("https://cdn/image/1/1.abc.def/1280x960");
        Assert.Equal(a, b);
        Assert.False(string.IsNullOrWhiteSpace(a));
    }
}

public sealed class AvitoNavigationDelaysTests
{
    [Fact]
    public void WithJitter_zero_jitter_is_exact()
    {
        Assert.Equal(1000, AvitoAgent.Avito.AvitoNavigationDelays.WithJitter(1000, 0));
    }

    [Fact]
    public void WithJitter_stays_in_range()
    {
        for (var i = 0; i < 40; i++)
        {
            var value = AvitoAgent.Avito.AvitoNavigationDelays.WithJitter(1000, 200);
            Assert.InRange(value, 1000, 1200);
        }
    }
}
