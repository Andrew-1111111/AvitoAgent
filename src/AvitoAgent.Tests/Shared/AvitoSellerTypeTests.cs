using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Tests.Shared;

public sealed class AvitoSellerTypeTests
{
    [Theory]
    [InlineData("Все", AvitoSellerTypeMode.All, "Все", null)]
    [InlineData("любые", AvitoSellerTypeMode.All, "Все", null)]
    [InlineData("Частные", AvitoSellerTypeMode.Private, "Частные", "1")]
    [InlineData("частник", AvitoSellerTypeMode.Private, "Частные", "1")]
    [InlineData("Компании", AvitoSellerTypeMode.Company, "Компании", "2")]
    [InlineData("магазины", AvitoSellerTypeMode.Company, "Компании", "2")]
    public void TryResolve_and_UrlValue(
        string input,
        AvitoSellerTypeMode mode,
        string canonical,
        string? url
    )
    {
        Assert.True(AvitoSellerType.TryResolve(input, out var name, out var resolved));
        Assert.Equal(mode, resolved);
        Assert.Equal(canonical, name);
        Assert.Equal(url, AvitoSellerType.UrlValue(mode));
        Assert.Equal(mode, AvitoSellerType.Parse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("боты")]
    public void Rejects_invalid(string? input)
    {
        Assert.False(AvitoSellerType.TryResolve(input, out _, out _));
    }

    [Fact]
    public void OptionLabels_private_includes_aliases()
    {
        Assert.Contains("Частные лица", AvitoSellerType.OptionLabels(AvitoSellerTypeMode.Private));
    }
}
