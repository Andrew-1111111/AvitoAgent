using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AvitoAgent.Avito.Options;

internal sealed class AvitoOptionsValidator : IValidateOptions<AvitoOptions>
{
    public ValidateOptionsResult Validate(string? name, AvitoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Filters ??= new();
        options.Auth ??= new();

        var failures = new List<string>();

        SettingsError.RequireHttpUrl(failures, "Avito:BaseUrl", options.BaseUrl);

        if (options.GetLocationSlugs().Count == 0)
        {
            failures.Add(
                SettingsError.Required("Avito:Filters:LocationSlug", "Укажите хотя бы один регион.")
            );
        }

        if (!AvitoSort.IsAllowed(options.Filters.Sort))
        {
            failures.Add(AvitoSort.FormatInvalidSort(options.Filters.Sort));
        }

        if (!AvitoCondition.IsAllowed(options.Filters.Condition))
        {
            failures.Add(AvitoCondition.FormatInvalid(options.Filters.Condition));
        }

        if (!AvitoSellerType.IsAllowed(options.Filters.SellerType))
        {
            failures.Add(AvitoSellerType.FormatInvalid(options.Filters.SellerType));
        }

        SettingsError.RequireRange(failures, "Avito:MaxPages", options.MaxPages, 1, int.MaxValue);
        SettingsError.RequireRange(
            failures,
            "Avito:NavigationTimeoutMs",
            options.NavigationTimeoutMs,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:SearchWarmupMs",
            options.SearchWarmupMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:SearchWarmupJitterMs",
            options.SearchWarmupJitterMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:DetailDelayMs",
            options.DetailDelayMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:DetailDelayJitterMs",
            options.DetailDelayJitterMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:DetailPageSettleMs",
            options.DetailPageSettleMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:DetailPageSettleJitterMs",
            options.DetailPageSettleJitterMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:DetailDelayAfterMs",
            options.DetailDelayAfterMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:DetailDelayAfterJitterMs",
            options.DetailDelayAfterJitterMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:SearchPageDelayMs",
            options.SearchPageDelayMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:SearchPageDelayJitterMs",
            options.SearchPageDelayJitterMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:SearchResultsWaitMs",
            options.SearchResultsWaitMs,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:MaxCardsPerSearchPage",
            options.MaxCardsPerSearchPage,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:MaxDetailsPerSearch",
            options.MaxDetailsPerSearch,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:ManualCaptchaWaitMs",
            options.ManualCaptchaWaitMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:ManualCaptchaPollMs",
            options.ManualCaptchaPollMs,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:PostCaptchaDelayMs",
            options.PostCaptchaDelayMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:DetailGalleryInitialDelayMs",
            options.DetailGalleryInitialDelayMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:DetailGalleryInitialDelayJitterMs",
            options.DetailGalleryInitialDelayJitterMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:GalleryPhotoDelayMs",
            options.GalleryPhotoDelayMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:GalleryPhotoDelayJitterMs",
            options.GalleryPhotoDelayJitterMs,
            0,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:MaxGalleryPhotos",
            options.MaxGalleryPhotos,
            0,
            int.MaxValue
        );

        ValidateAuth(options.Auth, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateAuth(AvitoAuthOptions auth, List<string> failures)
    {
        SettingsError.RequireRange(
            failures,
            "Avito:Auth:LoginTimeoutMs",
            auth.LoginTimeoutMs,
            1,
            int.MaxValue
        );
        SettingsError.RequireRange(
            failures,
            "Avito:Auth:FormSearchTimeoutMs",
            auth.FormSearchTimeoutMs,
            1,
            int.MaxValue
        );
        SettingsError.RequireNotEmpty(
            failures,
            "Avito:Auth:StorageStateFile",
            auth.StorageStateFile,
            "Укажите имя файла сессии."
        );

        if (!auth.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(auth.Phone) && string.IsNullOrWhiteSpace(auth.Email))
        {
            failures.Add(
                SettingsError.Required(
                    "Avito:Auth:Phone или Avito:Auth:Email",
                    "Укажите телефон или email."
                )
            );
        }

        SettingsError.RequireNotEmpty(
            failures,
            "Avito:Auth:Password",
            auth.Password,
            "Укажите пароль."
        );
    }
}
