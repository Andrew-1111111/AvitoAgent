using AvitoAgent.Playwright.Interfaces;
using AvitoAgent.Playwright.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AvitoAgent.Playwright.Browser;

public sealed class BrowserSession(IBrowser? browser, IBrowserContext context, ILogger logger)
    : IBrowserSession
{
    private readonly ILogger _logger = logger;
    private bool _disposed;

    public IBrowser? Browser { get; } = browser;

    public IBrowserContext Context { get; } = context;

    public IPageFactory Pages { get; } = new PageFactory(context, logger);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await Context.CloseAsync();

            if (Browser is not null)
            {
                await Browser.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            PlaywrightLog.OperationFailed(_logger, ex);
        }
        finally
        {
            PlaywrightLog.SessionDisposed(_logger);
        }
    }
}
