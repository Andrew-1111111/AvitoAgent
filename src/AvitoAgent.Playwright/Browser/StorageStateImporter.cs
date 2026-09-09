using System.Text.Json;
using Microsoft.Playwright;

namespace AvitoAgent.Playwright.Browser;

internal static class StorageStateImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] AvitoSessionCookieHints =
    [
        "auth",
        "u",
        "sessid",
        "fxf",
        "buyer_location_id",
        "srv_id",
    ];

    public static async Task ImportIfNeededAsync(
        IBrowserContext context,
        string? storageStatePath,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(storageStatePath) || !File.Exists(storageStatePath))
        {
            return;
        }

        // Даже если каталог профиля не пуст — без cookies Avito подтягиваем backup JSON.
        if (await HasAvitoSessionCookiesAsync(context))
        {
            return;
        }

        await using var stream = File.OpenRead(storageStatePath);
        var state = await JsonSerializer.DeserializeAsync<StorageStateDocument>(
            stream,
            JsonOptions,
            cancellationToken
        );

        if (state?.Cookies is null || state.Cookies.Count == 0)
        {
            return;
        }

        var cookies = state
            .Cookies.Where(cookie =>
                !string.IsNullOrWhiteSpace(cookie.Name) && cookie.Value is not null
            )
            .Select(cookie => new Cookie
            {
                Name = cookie.Name!,
                Value = cookie.Value!,
                Domain = cookie.Domain ?? ".avito.ru",
                Path = cookie.Path ?? "/",
                Expires = cookie.Expires,
                HttpOnly = cookie.HttpOnly ?? false,
                Secure = cookie.Secure ?? false,
                SameSite = ParseSameSite(cookie.SameSite),
            })
            .ToList();

        if (cookies.Count == 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await context.AddCookiesAsync(cookies);
    }

    private static async Task<bool> HasAvitoSessionCookiesAsync(IBrowserContext context)
    {
        try
        {
            var cookies = await context.CookiesAsync(
                ["https://www.avito.ru/", "https://avito.ru/"]
            );
            return cookies.Any(cookie =>
                AvitoSessionCookieHints.Any(hint =>
                    cookie.Name.Equals(hint, StringComparison.OrdinalIgnoreCase)
                )
            );
        }
        catch
        {
            return false;
        }
    }

    private static SameSiteAttribute? ParseSameSite(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "strict" => SameSiteAttribute.Strict,
            "lax" => SameSiteAttribute.Lax,
            "none" => SameSiteAttribute.None,
            _ => null,
        };

    private sealed class StorageStateDocument
    {
        public List<StorageCookie>? Cookies { get; init; }
    }

    private sealed class StorageCookie
    {
        public string? Name { get; init; }

        public string? Value { get; init; }

        public string? Domain { get; init; }

        public string? Path { get; init; }

        public float? Expires { get; init; }

        public bool? HttpOnly { get; init; }

        public bool? Secure { get; init; }

        public string? SameSite { get; init; }
    }
}
