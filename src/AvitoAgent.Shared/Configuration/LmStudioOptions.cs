namespace AvitoAgent.Shared.Configuration;

public sealed class LmStudioOptions
{
    public const string SectionName = "LmStudio";

    /// <summary>
    /// Отправлять объявления в LM Studio. false - без анализа и без проверки, что сервер запущен.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:1234/v1/";

    public string Model { get; set; } = "qwen/qwen3.5-9b";

    /// <summary>
    /// Если указанная модель не найдена - взять первую из списка LM Studio.
    /// </summary>
    public bool AutoSelectModel { get; set; } = true;

    /// <summary>
    /// При проверке LM Studio загружать модель в память через API, если она ещё не loaded.
    /// </summary>
    public bool AutoLoadModel { get; set; } = true;

    /// <summary>
    /// context_length при автозагрузке (POST /api/v1/models/load). 0 - не указывать (дефолт LM Studio).
    /// Для vision лучше 8192-16384, не 262144.
    /// </summary>
    public int LoadContextLength { get; set; } = 32_768;

    /// <summary>
    /// json_schema в LM Studio. Для Qwen3.5 оставляйте false: schema конфликтует с think-prefill.
    /// </summary>
    public bool UseJsonSchemaResponse { get; set; }

    /// <summary>
    /// Максимум фото в LM Studio. 0 - сколько влезает в бюджет контекста.
    /// </summary>
    public int MaxImages { get; set; } = 1;

    /// <summary>
    /// Доля доступного контекста модели под текст+фото+ответ (1-100).
    /// </summary>
    public int ContextUsagePercent { get; set; } = 50;

    /// <summary>
    /// Запас токенов на JSON-ответ модели.
    /// </summary>
    public int MaxCompletionTokens { get; set; } = 800;

    /// <summary>
    /// Уменьшать фото перед LM Studio. false - исходный размер (WebP всё равно → JPEG без resize).
    /// </summary>
    public bool OptimizeImages { get; set; }

    /// <summary>
    /// Максимальная сторона фото для LM Studio при OptimizeImages=true.
    /// </summary>
    public int MaxImageSidePx { get; set; } = 1_024;

    /// <summary>
    /// Качество JPEG, только если фото пришлось уменьшить.
    /// </summary>
    public int ImageJpegQuality { get; set; } = 80;

    /// <summary>
    /// Не отправлять system-сообщение в API - инструкции заданы в настройках модели LM Studio.
    /// </summary>
    public bool UseExternalSystemPrompt { get; set; }

    public double Temperature { get; set; } = 0.1;

    public int RequestTimeoutSeconds { get; set; } = 900;
}
