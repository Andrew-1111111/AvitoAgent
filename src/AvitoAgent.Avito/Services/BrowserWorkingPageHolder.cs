namespace AvitoAgent.Avito.Services;

using Microsoft.Playwright;

/// <summary>
/// Хранит рабочую вкладку браузера, чтобы не создавать новые при каждом цикле.
/// </summary>
public sealed class BrowserWorkingPageHolder
{
    private volatile IPage? _page;

    public IPage? Page => _page;

    public void SetPage(IPage page) => _page = page;
}
