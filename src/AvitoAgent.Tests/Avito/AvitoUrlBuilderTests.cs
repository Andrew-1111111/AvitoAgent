using AvitoAgent.Avito;
using AvitoAgent.Core.Models;
using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Tests.Avito;

public sealed class AvitoUrlBuilderTests
{
    private static AvitoOptions Options(params string[] locations) =>
        new()
        {
            BaseUrl = "https://www.avito.ru",
            Filters = new AvitoFiltersOptions { LocationSlug = locations },
        };

    [Fact]
    public void ResolveLocation_prefers_criteria()
    {
        var options = Options("rossiya");
        var criteria = new SearchCriteria { LocationSlugs = ["mytischi"] };
        Assert.Equal("mytischi", AvitoUrlBuilder.ResolveLocation(options, criteria));
        Assert.Equal("rossiya", AvitoUrlBuilder.ResolveLocation(options));
    }

    [Fact]
    public void BuildSearchUrl_region_with_filters_no_q()
    {
        var url = AvitoUrlBuilder.BuildSearchUrl(
            Options("moskovskaya_oblast"),
            new SearchCriteria
            {
                Keywords = ["генератор"],
                LocationSlugs = ["moskovskaya_oblast"],
                MinPrice = 1000,
                MaxPrice = 50000,
                DeliveryOnly = true,
                Condition = "Б/у",
                SellerType = "Частные",
            },
            page: 2
        );

        Assert.StartsWith("https://www.avito.ru/moskovskaya_oblast?", url);
        Assert.DoesNotContain("q=", url);
        Assert.Contains("localPriority=1", url);
        Assert.Contains("pmin=1000", url);
        Assert.Contains("pmax=50000", url);
        Assert.Contains("condition=2", url);
        Assert.Contains("d=1", url);
        Assert.Contains("user=1", url);
        Assert.Contains("p=2", url);
    }

    [Fact]
    public void BuildSearchUrl_nationwide_skips_localPriority()
    {
        var url = AvitoUrlBuilder.BuildSearchUrl(
            Options("rossiya"),
            new SearchCriteria { LocationSlugs = ["rossiya"] },
            page: 1
        );
        Assert.Equal("https://www.avito.ru/rossiya", url);
        Assert.True(AvitoUrlBuilder.IsNationwide("rossiya"));
        Assert.True(AvitoUrlBuilder.IsNationwide("all"));
        Assert.False(AvitoUrlBuilder.IsNationwide("moskva"));
    }

    [Fact]
    public void TryMergeCriteriaFilters_updates_query()
    {
        var ok = AvitoUrlBuilder.TryMergeCriteriaFilters(
            "https://www.avito.ru/moskva/transport?q=test&context=abc",
            new SearchCriteria
            {
                DeliveryOnly = true,
                Condition = "Новое",
                SellerType = "Компании",
                MinPrice = 10,
            },
            out var target
        );

        Assert.True(ok);
        Assert.Contains("d=1", target);
        Assert.Contains("condition=1", target);
        Assert.Contains("user=2", target);
        Assert.Contains("pmin=10", target);
        Assert.Contains("localPriority=1", target);
        Assert.Contains("q=test", target);
    }

    [Fact]
    public void HasCriteriaFilters_checks_required_params()
    {
        var criteria = new SearchCriteria
        {
            DeliveryOnly = true,
            Condition = "Б/у",
            SellerType = "Частные",
            MinPrice = 100,
        };
        Assert.True(
            AvitoUrlBuilder.HasCriteriaFilters(
                "https://www.avito.ru/x?d=1&condition=2&user=1&pmin=100",
                criteria
            )
        );
        Assert.False(
            AvitoUrlBuilder.HasCriteriaFilters("https://www.avito.ru/x?d=1&condition=2", criteria)
        );
    }

    [Fact]
    public void SearchUrlsAligned_ignores_context()
    {
        Assert.True(
            AvitoUrlBuilder.SearchUrlsAligned(
                "https://www.avito.ru/moskva?d=1&context=zzz",
                "https://www.avito.ru/moskva?d=1"
            )
        );
        Assert.False(
            AvitoUrlBuilder.SearchUrlsAligned(
                "https://www.avito.ru/moskva?d=1",
                "https://www.avito.ru/tver?d=1"
            )
        );
    }

    [Fact]
    public void SearchLocationMatches_compares_first_segment()
    {
        Assert.True(
            AvitoUrlBuilder.SearchLocationMatches(
                "https://www.avito.ru/moskva/auto",
                "https://www.avito.ru/moskva"
            )
        );
        Assert.False(
            AvitoUrlBuilder.SearchLocationMatches(
                "https://www.avito.ru/moskva",
                "https://www.avito.ru/tver"
            )
        );
    }

    [Fact]
    public void IsConditionApplied_and_query_helpers()
    {
        Assert.True(AvitoUrlBuilder.IsConditionApplied("https://x/y", AvitoConditionMode.All));
        Assert.True(
            AvitoUrlBuilder.IsConditionApplied(
                "https://www.avito.ru/x?condition=1",
                AvitoConditionMode.New
            )
        );
        Assert.False(
            AvitoUrlBuilder.IsConditionApplied(
                "https://www.avito.ru/x?condition=2",
                AvitoConditionMode.New
            )
        );

        Assert.True(AvitoUrlBuilder.UrlMatchesSearchQuery("https://x/y", " "));
        Assert.True(
            AvitoUrlBuilder.TryGetSearchQuery(
                "https://www.avito.ru/x?q=foo%20bar&d=1",
                out var q
            )
        );
        Assert.Equal("foo bar", q);
        Assert.True(AvitoUrlBuilder.UrlMatchesSearchQuery("https://www.avito.ru/x?q=foo+bar", "foo  bar"));
        Assert.False(AvitoUrlBuilder.UrlMatchesSearchQuery("https://www.avito.ru/x", "foo"));
    }

    [Fact]
    public void BuildSearchUrl_page1_omits_p_and_skips_invalid_filters()
    {
        var url = AvitoUrlBuilder.BuildSearchUrl(
            Options("moskva"),
            new SearchCriteria
            {
                Keywords = ["мотор"],
                LocationSlugs = ["moskva"],
                Condition = "неизвестно",
                SellerType = "неизвестно",
            },
            page: 1
        );

        Assert.DoesNotContain("p=", url);
        Assert.DoesNotContain("condition=", url);
        Assert.DoesNotContain("user=", url);
        Assert.Contains("localPriority=1", url);
        Assert.DoesNotContain("q=", url);
    }

    [Fact]
    public void TryMergeCriteriaFilters_rejects_relative_url()
    {
        Assert.False(
            AvitoUrlBuilder.TryMergeCriteriaFilters(
                "/search",
                new SearchCriteria { DeliveryOnly = true },
                out _
            )
        );
    }
}
