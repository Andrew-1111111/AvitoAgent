using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Tests.Shared;

public sealed class AvitoFiltersOptionsTests
{
    [Fact]
    public void GetLocationSlugs_splits_comma_and_dedupes()
    {
        var options = new AvitoFiltersOptions
        {
            LocationSlug = ["mytischi, tver", "Tver", "  ", "moskva"],
        };

        Assert.Equal(["mytischi", "tver", "moskva"], options.GetLocationSlugs());
    }

    [Fact]
    public void GetLocationSlugs_empty_falls_back_to_rossiya()
    {
        var options = new AvitoFiltersOptions { LocationSlug = ["", "  "] };
        Assert.Equal(["rossiya"], options.GetLocationSlugs());
    }

    [Fact]
    public void GetLocationSlugs_trims_slashes()
    {
        var options = new AvitoFiltersOptions { LocationSlug = ["/moskva/"] };
        Assert.Equal(["moskva"], options.GetLocationSlugs());
    }
}
