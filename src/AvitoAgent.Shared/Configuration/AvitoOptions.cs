namespace AvitoAgent.Shared.Configuration;

public sealed class AvitoOptions
{
    public const string SectionName = "Avito";

    public AvitoFiltersOptions Filters { get; set; } = new();

    public IReadOnlyList<string> GetLocationSlugs() => Filters.GetLocationSlugs();

    public bool DeliveryOnly => Filters.DeliveryOnly;

    public string BaseUrl { get; set; } = "https://www.avito.ru";

    /// <summary>
    /// Сколько страниц выдачи обработать (от новых к старым: 1, 2, …).
    /// </summary>
    public int MaxPages { get; set; } = 3;

    public int NavigationTimeoutMs { get; set; } = 60_000;

    public int SearchWarmupMs { get; set; } = 2_500;

    public int SearchWarmupJitterMs { get; set; } = 1_500;

    public bool NavigateViaHomepage { get; set; } = true;

    public int DetailDelayMs { get; set; } = 3_000;

    public int DetailDelayJitterMs { get; set; } = 2_000;

    public int DetailPageSettleMs { get; set; } = 800;

    public int DetailPageSettleJitterMs { get; set; } = 400;

    public int DetailDelayAfterMs { get; set; } = 1_500;

    public int DetailDelayAfterJitterMs { get; set; } = 1_000;

    public int SearchPageDelayMs { get; set; } = 3_000;

    public int SearchPageDelayJitterMs { get; set; } = 2_000;

    public int SearchResultsWaitMs { get; set; } = 20_000;

    /// <summary>
    /// Максимум карточек, которые подгружаем прокруткой на одной странице выдачи.
    /// </summary>
    public int MaxCardsPerSearchPage { get; set; } = 50;

    /// <summary>
    /// Сколько объявлений открывать с одной выдачи. 0 - все собранные карточки.
    /// </summary>
    public int MaxDetailsPerSearch { get; set; }

    public bool ClickListingLinks { get; set; } = true;

    public bool StopOnBlock { get; set; } = true;

    /// <summary>
    /// Сколько ждать ручного прохождения капчи (мс). 0 - ждать бесконечно.
    /// </summary>
    public int ManualCaptchaWaitMs { get; set; } = 0;

    public int ManualCaptchaPollMs { get; set; } = 2_000;

    public int PostCaptchaDelayMs { get; set; } = 15_000;

    /// <summary>
    /// Пауза между переключениями фото в галерее (мс).
    /// </summary>
    public int GalleryPhotoDelayMs { get; set; } = 1400;

    public int GalleryPhotoDelayJitterMs { get; set; } = 900;

    /// <summary>
    /// Пауза после открытия объявления перед просмотром галереи (мс).
    /// </summary>
    public int DetailGalleryInitialDelayMs { get; set; } = 900;

    public int DetailGalleryInitialDelayJitterMs { get; set; } = 600;

    /// <summary>
    /// Лимит фото при просмотре галереи. 0 - без лимита (до 50).
    /// </summary>
    public int MaxGalleryPhotos { get; set; }

    public AvitoAuthOptions Auth { get; set; } = new();
}
