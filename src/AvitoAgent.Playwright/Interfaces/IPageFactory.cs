using Microsoft.Playwright;

namespace AvitoAgent.Playwright.Interfaces;

public interface IPageFactory
{
    Task<IPage> CreatePageAsync(CancellationToken cancellationToken = default);
}
