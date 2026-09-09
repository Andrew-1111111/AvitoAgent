using AvitoAgent.Avito;

namespace AvitoAgent.Tests.Avito;

public sealed class AvitoGeoTests
{
    [Theory]
    [InlineData("Москва", "moskva")]
    [InlineData("moskva", "moskva")]
    [InlineData("Мытищи", "mytishchi")]
    [InlineData("Московская область", "moskovskaya_oblast")]
    public void TryResolveInput_known(string input, string expectedSlug)
    {
        Assert.True(AvitoGeo.TryResolveInput(input, out var slug, out var display));
        Assert.Equal(expectedSlug, slug);
        Assert.False(string.IsNullOrWhiteSpace(display));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("планета марс")]
    public void TryResolveInput_unknown(string? input)
    {
        Assert.False(AvitoGeo.TryResolveInput(input, out _, out _));
    }

    [Fact]
    public void DisplayName_and_IsNationwide_subjects()
    {
        Assert.Contains("Москва", AvitoGeo.DisplayName("moskva"), StringComparison.OrdinalIgnoreCase);
        Assert.True(AvitoGeo.LooksLikeFederalSubject("Московская область"));
        Assert.False(AvitoGeo.LooksLikeFederalSubject("Мытищи"));
    }

    [Fact]
    public void TryFromUrl_extracts_city()
    {
        Assert.True(
            AvitoGeo.TryFromUrl("https://www.avito.ru/moskva/transport/item_123", out var city, out _)
        );
        Assert.False(string.IsNullOrWhiteSpace(city));
    }

    [Fact]
    public void TryBelongsToSearchLocation_city_vs_region()
    {
        // Same city listing under city search
        var sameCity = AvitoGeo.TryBelongsToSearchLocation(
            "https://www.avito.ru/mytischi/tovary/xxx",
            "Мытищи",
            "mytischi"
        );
        Assert.True(sameCity is null or true);

        var nationwide = AvitoGeo.TryBelongsToSearchLocation(
            "https://www.avito.ru/mytischi/tovary/xxx",
            "Мытищи",
            "rossiya"
        );
        Assert.NotEqual(false, nationwide);
    }

    [Fact]
    public void FormatListingAddress_empty_fallback()
    {
        Assert.Equal("не указан", AvitoGeo.FormatListingAddress(null, null));
        Assert.False(string.IsNullOrWhiteSpace(AvitoGeo.FormatListingAddress("Москва", null)));
    }

    [Fact]
    public void TryFromSearchSlug_region()
    {
        Assert.True(AvitoGeo.TryFromSearchSlug("moskovskaya_oblast", out var region));
        Assert.Contains("Москов", region);
    }

    [Fact]
    public void InferRegion_and_FormatSlugCatalog()
    {
        Assert.Contains("область", AvitoGeo.InferRegion(["Мытищи", "Московская область"])!);
        Assert.Equal("Москва", AvitoGeo.InferRegion(["Москва"]));
        Assert.Null(AvitoGeo.InferRegion(["планета марс"]));

        var catalog = AvitoGeo.FormatSlugCatalog();
        Assert.False(string.IsNullOrWhiteSpace(catalog));
        Assert.Contains("moskva", catalog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBelongsToSearchLocation_mismatch_city()
    {
        var mismatch = AvitoGeo.TryBelongsToSearchLocation(
            "https://www.avito.ru/tver/tovary/xxx",
            "Москва",
            "moskva"
        );
        Assert.NotEqual(true, mismatch);
    }
}
