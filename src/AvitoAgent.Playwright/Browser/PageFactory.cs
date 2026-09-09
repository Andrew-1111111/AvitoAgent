using AvitoAgent.Playwright.Exceptions;
using AvitoAgent.Playwright.Interfaces;
using AvitoAgent.Playwright.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AvitoAgent.Playwright.Browser;

internal sealed class PageFactory(IBrowserContext context, ILogger logger) : IPageFactory
{
    private readonly IBrowserContext _context = context;
    private readonly ILogger _logger = logger;

    public async Task<IPage> CreatePageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var page = await _context.NewPageAsync();
            cancellationToken.ThrowIfCancellationRequested();
            page.SetDefaultTimeout(30000);
            page.SetDefaultNavigationTimeout(60000);
            return page;
        }
        catch (Microsoft.Playwright.PlaywrightException ex)
        {
            throw new Exceptions.PlaywrightException("Не удалось создать страницу браузера.", ex);
        }
    }
}
