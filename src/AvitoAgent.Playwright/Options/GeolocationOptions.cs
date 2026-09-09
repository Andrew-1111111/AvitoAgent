namespace AvitoAgent.Playwright.Options;

/// <summary>
/// Browser geolocation settings.
/// </summary>
public sealed class GeolocationOptions
{
    /// <summary>
    /// Latitude.
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// Longitude.
    /// </summary>
    public double Longitude { get; init; }

    /// <summary>
    /// Accuracy in meters.
    /// </summary>
    public double Accuracy { get; init; } = 0;

    /// <summary>
    /// Gets whether geolocation is configured.
    /// </summary>
    public bool IsConfigured => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}
