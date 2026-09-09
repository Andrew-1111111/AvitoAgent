using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Tests.Shared;

public sealed class AvitoSortTests
{
    [Theory]
    [InlineData("По умолчанию", AvitoSortMode.Default, 101)]
    [InlineData("Дешевле", AvitoSortMode.Cheaper, 1)]
    [InlineData("Дороже", AvitoSortMode.Expensive, 2)]
    [InlineData("По дате", AvitoSortMode.Date, 104)]
    [InlineData("По размеру скидки", AvitoSortMode.Discount, 105)]
    public void TryResolve_and_UrlValue(string input, AvitoSortMode mode, int urlValue)
    {
        Assert.True(AvitoSort.TryResolve(input, out var canonical, out var resolved));
        Assert.Equal(mode, resolved);
        Assert.Equal(input, canonical);
        Assert.Equal(urlValue, AvitoSort.UrlValue(mode));
        Assert.Equal(mode, AvitoSort.Parse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("рандом")]
    public void Rejects_invalid(string? input)
    {
        Assert.False(AvitoSort.TryResolve(input, out _, out _));
    }

    [Theory]
    [InlineData("https://www.avito.ru/all?s=1", AvitoSortMode.Cheaper, true)]
    [InlineData("https://www.avito.ru/all?s=2", AvitoSortMode.Expensive, true)]
    [InlineData("https://www.avito.ru/all?s=104", AvitoSortMode.Date, true)]
    [InlineData("https://www.avito.ru/all", AvitoSortMode.Default, true)]
    [InlineData("https://www.avito.ru/all?s=101", AvitoSortMode.Default, true)]
    [InlineData("https://www.avito.ru/all?s=105", AvitoSortMode.Discount, true)]
    [InlineData("https://www.avito.ru/all?s=172297_desc", AvitoSortMode.Discount, true)]
    [InlineData("https://www.avito.ru/all?s=1", AvitoSortMode.Date, false)]
    [InlineData("not-a-url", AvitoSortMode.Cheaper, false)]
    public void UrlMatches(string url, AvitoSortMode mode, bool expected)
    {
        Assert.Equal(expected, AvitoSort.UrlMatches(url, mode));
    }

    [Theory]
    [InlineData("Сортировка", AvitoSortMode.Default, true)]
    [InlineData("По умолчанию", AvitoSortMode.Default, true)]
    [InlineData("Дешевле", AvitoSortMode.Cheaper, true)]
    [InlineData("Дешевле", AvitoSortMode.Date, false)]
    [InlineData("", AvitoSortMode.Default, false)]
    public void TriggerShows(string text, AvitoSortMode mode, bool expected)
    {
        Assert.Equal(expected, AvitoSort.TriggerShows(text, mode));
    }

    [Fact]
    public void IsApplied_prefers_url_for_non_default()
    {
        Assert.True(
            AvitoSort.IsApplied("https://www.avito.ru/all?s=104", "что угодно", AvitoSortMode.Date)
        );
        Assert.True(AvitoSort.IsApplied("https://www.avito.ru/all", "По дате", AvitoSortMode.Date));
    }

    [Fact]
    public void OptionMarker_discount_is_special()
    {
        Assert.Equal("172297_desc", AvitoSort.OptionMarker(AvitoSortMode.Discount));
    }
}
