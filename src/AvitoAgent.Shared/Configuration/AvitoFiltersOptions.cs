namespace AvitoAgent.Shared.Configuration;

public sealed class AvitoFiltersOptions
{
    public const string SectionName = "Avito:Filters";

    /// <summary>
    /// Регионы в URL Avito. Несколько — массив или строка через запятую.
    /// Каждый регион обходится отдельно; MaxResults действует на регион.
    /// </summary>
    public string[] LocationSlug { get; set; } = ["rossiya"];

    public IReadOnlyList<string> GetLocationSlugs()
    {
        var slugs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in LocationSlug ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (
                var part in raw.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                var slug = part.Trim('/');
                if (slug.Length > 0 && seen.Add(slug))
                {
                    slugs.Add(slug);
                }
            }
        }

        return slugs.Count > 0 ? slugs : ["rossiya"];
    }

    /// <summary>
    /// Выбранная сортировка выдачи. Должна совпадать с одним из SortValues.
    /// </summary>
    public string Sort { get; set; } = "По дате";

    /// <summary>
    /// Допустимые значения Sort. Задаются жёстко, не менять.
    /// </summary>
    public string[] SortValues { get; set; } = [.. AvitoSort.AllowedValues];

    /// <summary>
    /// true — искать и парсить только объявления с Авито Доставкой.
    /// </summary>
    public bool DeliveryOnly { get; set; }

    /// <summary>
    /// Состояние товара: Все, Новое, Б/у.
    /// Берётся со страницы объявления, на выдаче такого фильтра нет.
    /// </summary>
    public string Condition { get; set; } = "Все";

    /// <summary>
    /// Допустимые значения Condition. Задаются жёстко, не менять.
    /// </summary>
    public string[] ConditionValues { get; set; } = [.. AvitoCondition.AllowedValues];

    /// <summary>
    /// Тип продавца: Все, Частные, Компании.
    /// </summary>
    public string SellerType { get; set; } = "Все";

    /// <summary>
    /// Допустимые значения SellerType. Задаются жёстко, не менять.
    /// </summary>
    public string[] SellerTypeValues { get; set; } = [.. AvitoSellerType.AllowedValues];

    /// <summary>
    /// true — брать только объявления новее момента старта поиска (кнопка «Старт» / запуск приложения).
    /// </summary>
    public bool FromCurrentDateTime { get; set; }
}
