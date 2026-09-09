using AvitoAgent.Core.Interfaces;

namespace AvitoAgent.Avito.Services;

public sealed class AvitoLocationCatalog : ILocationCatalog
{
    public bool TryResolve(string input, out string slug, out string displayName) =>
        AvitoGeo.TryResolveInput(input, out slug, out displayName);

    public string DisplayName(string slug) => AvitoGeo.DisplayName(slug);

    public string FormatSlugCatalog() => Catalog.Value;

    private static readonly Lazy<string> Catalog = new(AvitoGeo.FormatSlugCatalog);
}
