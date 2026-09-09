namespace AvitoAgent.Core.Interfaces;

public interface ILocationCatalog
{
    bool TryResolve(string input, out string slug, out string displayName);

    string DisplayName(string slug);

    string FormatSlugCatalog();
}
