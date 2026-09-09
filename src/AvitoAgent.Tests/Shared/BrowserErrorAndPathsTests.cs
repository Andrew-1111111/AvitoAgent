using AvitoAgent.Shared;
using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Tests.Shared;

public sealed class BrowserErrorTextTests
{
    [Fact]
    public void Describe_maps_profile_in_use()
    {
        var text = BrowserErrorText.Describe(
            new InvalidOperationException("user data directory is already in use")
        );
        Assert.Contains("профиль Chrome уже занят", text);
    }

    [Fact]
    public void Describe_maps_timeout_and_closed()
    {
        Assert.Equal(
            "страница не ответила вовремя",
            BrowserErrorText.Describe(new TimeoutException("Timeout 30000ms exceeded"))
        );
        Assert.Equal(
            "окно браузера закрыто",
            BrowserErrorText.Describe(new InvalidOperationException("Target page, context or browser has been closed"))
        );
        Assert.Equal("поиск остановлен", BrowserErrorText.Describe(new OperationCanceledException()));
    }

    [Fact]
    public void IsTimeout_IsCanceled_IsBrowserClosed()
    {
        Assert.True(BrowserErrorText.IsTimeout(new TimeoutException("Timeout")));
        Assert.True(BrowserErrorText.IsOperationCanceled(new TaskCanceledException()));
        Assert.True(
            BrowserErrorText.IsBrowserClosed(new InvalidOperationException("Browser has been closed"))
        );
        Assert.False(BrowserErrorText.IsTimeout(new InvalidOperationException("other")));
    }

    [Fact]
    public void Describe_keeps_russian_message()
    {
        var text = BrowserErrorText.Describe(new InvalidOperationException("Не удалось открыть форму"));
        Assert.Equal("Не удалось открыть форму", text);
    }

    [Theory]
    [InlineData("Page crashed", "аварийно")]
    [InlineData("Execution context was destroyed", "обновилась")]
    [InlineData("net::ERR_INTERNET_DISCONNECTED", "интернет")]
    [InlineData("net::ERR_NAME_NOT_RESOLVED", "DNS")]
    [InlineData("net::ERR_CONNECTION_RESET", "сеть")]
    [InlineData("response ended prematurely", "LM Studio")]
    public void Describe_maps_more_browser_errors(string message, string expected)
    {
        Assert.Contains(
            expected,
            BrowserErrorText.Describe(new InvalidOperationException(message)),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Describe_empty_message_fallback()
    {
        Assert.Equal(
            "неизвестная ошибка браузера",
            BrowserErrorText.Describe(new InvalidOperationException("   "))
        );
    }
}

public sealed class Socks5OptionsTests
{
    [Theory]
    [InlineData(true, "127.0.0.1", 1080, true)]
    [InlineData(false, "127.0.0.1", 1080, false)]
    [InlineData(true, "", 1080, false)]
    [InlineData(true, "host", 0, false)]
    [InlineData(true, "host", 70000, false)]
    public void IsConfigured(bool enabled, string host, int port, bool expected)
    {
        var options = new Socks5Options
        {
            Enabled = enabled,
            Host = host,
            Port = port,
        };
        Assert.Equal(expected, options.IsConfigured);
    }
}

public sealed class ApplicationPathsTests
{
    [Fact]
    public void Resolves_relative_directories_under_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "avito-agent-paths-" + Guid.NewGuid());
        try
        {
            var paths = new ApplicationPaths(root);
            Assert.Equal(Path.GetFullPath(root), paths.Root);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "data")), paths.Data);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "logs")), paths.Logs);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "prompts")), paths.Prompts);
            Assert.EndsWith("avito.db", paths.DatabaseFile);
            Assert.EndsWith("browser-profile", paths.BrowserProfile);
            Assert.True(Directory.Exists(paths.Data));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
