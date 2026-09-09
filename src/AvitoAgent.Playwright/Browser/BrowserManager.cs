using AvitoAgent.Playwright.Interfaces;
using AvitoAgent.Playwright.Logging;
using AvitoAgent.Playwright.Options;
using AvitoAgent.Shared;
using AvitoAgent.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace AvitoAgent.Playwright.Browser;

public sealed class BrowserManager(
    IOptions<PlaywrightOptions> options,
    IOptions<DebugOptions> debug,
    ApplicationPaths paths,
    ILogger<BrowserManager> logger,
    ILoggerFactory loggerFactory
) : IBrowserManager
{
    private readonly PlaywrightOptions _options = options.Value;
    private readonly DebugOptions _debug = debug.Value;
    private readonly ApplicationPaths _paths = paths;
    private readonly ILogger<BrowserManager> _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IPlaywright? _playwright;
    private BrowserSession? _persistentSession;
    private bool _initializedLogged;
    private bool _disposed;

    public async Task<IBrowserSession> CreateSessionAsync(
        BrowserSessionRequest? request = null,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            return await CreateSessionInternalAsync(request, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IBrowserSession> GetPersistentSessionAsync(
        BrowserSessionRequest? request = null,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_persistentSession is not null && IsSessionAlive(_persistentSession))
            {
                return _persistentSession;
            }

            if (_persistentSession is not null)
            {
                PlaywrightLog.PersistentSessionRecreating(_logger);
                if (!string.IsNullOrWhiteSpace(request?.StorageStatePath))
                {
                    try
                    {
                        var path = request.StorageStatePath;
                        var directory = Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        await _persistentSession.Context.StorageStateAsync(
                            new BrowserContextStorageStateOptions { Path = path }
                        );
                        PlaywrightLog.StorageStateFlushed(_logger, path);
                    }
                    catch (Exception ex)
                    {
                        PlaywrightLog.Warning(
                            _logger,
                            "Не удалось сохранить cookies перед пересозданием сессии: "
                                + BrowserErrorText.Describe(ex)
                        );
                    }
                }

                await _persistentSession.DisposeAsync();
                _persistentSession = null;
            }

            _persistentSession = await CreateSessionInternalAsync(request, cancellationToken);
            PlaywrightLog.PersistentSessionStarted(_logger);

            return _persistentSession;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BrowserSession> CreateSessionInternalAsync(
        BrowserSessionRequest? request,
        CancellationToken cancellationToken
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync();

            if (!_initializedLogged)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    var engine = _options.Launch.Browser.ToString();
                    PlaywrightLog.Initialized(_logger, engine, _options.Launch.Headless);
                }

                _initializedLogged = true;
            }

            if (_options.UsePersistentProfile)
            {
                var userDataDir = ResolveUserDataDir();

                PlaywrightLog.PersistentProfileUsed(_logger, userDataDir);

                var context = await BrowserFactory.LaunchPersistentContextAsync(
                    _playwright,
                    _options,
                    userDataDir,
                    request?.StorageStatePath,
                    _logger,
                    cancellationToken
                );

                PlaywrightLog.SessionCreated(_logger);
                return CreateSession(context.Browser, context);
            }

            var browser = await BrowserFactory.LaunchAsync(
                _playwright,
                _options,
                _logger,
                cancellationToken
            );
            var ephemeralContext = await BrowserFactory.CreateContextAsync(
                browser,
                _options,
                request?.StorageStatePath,
                cancellationToken
            );

            PlaywrightLog.SessionCreated(_logger);
            return CreateSession(browser, ephemeralContext);
        }
        catch (Exception ex)
        {
            PlaywrightLog.OperationFailed(_logger, ex);
            throw;
        }
    }

    private BrowserSession CreateSession(IBrowser? browser, IBrowserContext context)
    {
        if (_debug.BrowserLog)
        {
            var debugLogger = _loggerFactory.CreateLogger(PlaywrightBrowserDebug.SourceContext);
            new PlaywrightBrowserDebug(debugLogger).Attach(context);
        }

        return new BrowserSession(browser, context, _logger);
    }

    public async Task ClosePersistentSessionAsync(
        string? storageStatePath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_persistentSession is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(storageStatePath))
            {
                try
                {
                    var directory = Path.GetDirectoryName(storageStatePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    await _persistentSession.Context.StorageStateAsync(
                        new BrowserContextStorageStateOptions { Path = storageStatePath }
                    );
                    PlaywrightLog.StorageStateFlushed(_logger, storageStatePath);
                }
                catch (Exception ex)
                {
                    PlaywrightLog.Warning(
                        _logger,
                        "Не удалось сохранить cookies перед закрытием: "
                            + BrowserErrorText.Describe(ex)
                    );
                }
            }

            await _persistentSession.DisposeAsync();
            _persistentSession = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string ResolveUserDataDir()
    {
        if (string.IsNullOrWhiteSpace(_options.UserDataDir))
        {
            return _paths.BrowserProfile;
        }

        var configured = _options.UserDataDir.Trim();
        return Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(_paths.Root, configured));
    }

    private static bool IsSessionAlive(BrowserSession session) =>
        session.Context.Browser?.IsConnected != false;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _gate.WaitAsync();

        try
        {
            if (_persistentSession is not null)
            {
                await _persistentSession.DisposeAsync();
                _persistentSession = null;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (_playwright is not null)
        {
            _playwright.Dispose();
            _playwright = null;
        }

        _gate.Dispose();
    }
}
