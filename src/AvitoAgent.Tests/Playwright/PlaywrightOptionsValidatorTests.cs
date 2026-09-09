using AvitoAgent.Playwright.Options;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Tests.Playwright;

public sealed class PlaywrightOptionsValidatorTests
{
    private readonly PlaywrightOptionsValidator _validator = new();

    [Fact]
    public void Valid_defaults_succeed()
    {
        Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, new PlaywrightOptions()));
    }

    [Fact]
    public void Invalid_channel_and_short_user_agent()
    {
        var options = new PlaywrightOptions
        {
            Launch = new LaunchOptions { Channel = "firefox" },
            Context = new ContextOptions { UserAgent = "short" },
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Channel", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("UserAgent", StringComparison.Ordinal));
    }

    [Fact]
    public void EmulateViewport_requires_positive_size()
    {
        var options = new PlaywrightOptions
        {
            Context = new ContextOptions
            {
                EmulateViewport = true,
                Viewport = new ViewportSize { Width = 0, Height = 0 },
                DeviceScaleFactor = 0,
            },
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Bad_geolocation_and_video_without_directory()
    {
        var options = new PlaywrightOptions
        {
            Context = new ContextOptions
            {
                Geolocation = new GeolocationOptions { Latitude = 999, Longitude = 0 },
                Video = new VideoRecordingOptions { Enabled = true, Directory = "" },
            },
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Geolocation", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("Video", StringComparison.Ordinal));
    }

    [Fact]
    public void Proxy_enabled_requires_absolute_server()
    {
        var empty = new PlaywrightOptions { Proxy = new ProxyOptions { Enabled = true, Server = "" } };
        Assert.True(_validator.Validate(null, empty).Failed);

        var relative = new PlaywrightOptions
        {
            Proxy = new ProxyOptions { Enabled = true, Server = "/relative-proxy" },
        };
        Assert.True(_validator.Validate(null, relative).Failed);

        var ok = new PlaywrightOptions
        {
            Proxy = new ProxyOptions { Enabled = true, Server = "http://127.0.0.1:8080" },
        };
        Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, ok));
    }

    [Fact]
    public void Proxy_disabled_skips_server()
    {
        var options = new PlaywrightOptions
        {
            Proxy = new ProxyOptions { Enabled = false, Server = "" },
        };
        Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, options));
    }

    [Theory]
    [InlineData(-91, 0, false)]
    [InlineData(0, 181, false)]
    [InlineData(55.75, 37.62, true)]
    public void GeolocationOptions_IsConfigured(double lat, double lon, bool expected)
    {
        Assert.Equal(
            expected,
            new GeolocationOptions { Latitude = lat, Longitude = lon }.IsConfigured
        );
    }
}
