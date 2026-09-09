using Microsoft.Playwright;

namespace AvitoAgent.Playwright.Interfaces;

public interface IBrowserSession : IAsyncDisposable
{
    IBrowser? Browser { get; }

    IBrowserContext Context { get; }

    IPageFactory Pages { get; }
}
