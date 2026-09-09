using System.Net.Sockets;
using AvitoAgent.AI.Services;
using AvitoAgent.Playwright.Browser;
using AvitoAgent.Telegram.Services;

namespace AvitoAgent.Tests.Infrastructure;

public sealed class TrackerBlocklistTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("not-a-url", false)]
    [InlineData("https://www.avito.ru/moskva", false)]
    [InlineData("https://sntr.avito.ru/event", false)]
    [InlineData("https://img.avito.st/image.jpg", false)]
    [InlineData("https://www.google-analytics.com/g/collect", true)]
    [InlineData("https://mc.yandex.ru/watch/1", true)]
    [InlineData("https://connect.facebook.net/en/fbevents.js", true)]
    [InlineData("https://www.google.com/ccm/collect", true)]
    [InlineData("https://example.com/pagead/ads", true)]
    [InlineData("https://example.com/ok", false)]
    public void ShouldBlock(string? url, bool expected)
    {
        Assert.Equal(expected, TrackerBlocklist.ShouldBlock(url));
    }
}

public sealed class TelegramErrorMapperTests
{
    [Fact]
    public void FromHttp_maps_auth_and_chat_errors()
    {
        Assert.Contains("BotToken", TelegramErrorMapper.FromHttp(401, null).Message);
        Assert.Contains("заблокирован", TelegramErrorMapper.FromHttp(403, null).Message);
        Assert.Contains(
            "ChatId",
            TelegramErrorMapper
                .FromHttp(400, """{"ok":false,"description":"Bad Request: chat not found"}""")
                .Message
        );
    }

    [Fact]
    public void IsOk_parses_json()
    {
        Assert.True(TelegramErrorMapper.IsOk("""{"ok":true}"""));
        Assert.False(TelegramErrorMapper.IsOk("""{"ok":false}"""));
        Assert.False(TelegramErrorMapper.IsOk("not-json"));
        Assert.False(TelegramErrorMapper.IsOk(null));
    }

    [Fact]
    public void DescribeNetwork_socks_and_timeout()
    {
        Assert.Contains(
            "SOCKS5",
            TelegramErrorMapper.DescribeNetwork(new InvalidOperationException("SOCKS5 failed"))
        );
        Assert.Contains(
            "Истекло время",
            TelegramErrorMapper.DescribeNetwork(new TaskCanceledException())
        );
    }

    [Fact]
    public void FromHttp_default_branch_and_network_wrapper()
    {
        var generic = TelegramErrorMapper.FromHttp(429, """{"description":"Too Many Requests"}""");
        Assert.Contains("Too Many Requests", generic.Message);
        Assert.Contains("429", generic.Message);

        var wrapped = TelegramErrorMapper.FromNetwork(new HttpRequestException("down"));
        Assert.Contains("Telegram недоступен", wrapped.Message);
    }

    [Fact]
    public void DescribeNetwork_more_socket_codes()
    {
        Assert.Contains(
            "таймаут",
            TelegramErrorMapper.DescribeNetwork(
                new HttpRequestException("fail", new SocketException((int)SocketError.TimedOut))
            ),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "отклонил",
            TelegramErrorMapper.DescribeNetwork(
                new HttpRequestException(
                    "fail",
                    new SocketException((int)SocketError.ConnectionRefused)
                )
            ),
            StringComparison.OrdinalIgnoreCase
        );
    }
}

public sealed class NetworkInterfaceResolverTests
{
    [Fact]
    public void Empty_resolves_to_null()
    {
        Assert.True(
            AvitoAgent.Infrastructure.Http.NetworkInterfaceResolver.TryResolve(
                "",
                out var address,
                out var error
            )
        );
        Assert.Null(address);
        Assert.Empty(error);
    }

    [Fact]
    public void Loopback_is_accepted()
    {
        Assert.True(
            AvitoAgent.Infrastructure.Http.NetworkInterfaceResolver.TryResolve(
                "127.0.0.1",
                out var address,
                out _
            )
        );
        Assert.Equal(System.Net.IPAddress.Loopback, address);
    }

    [Fact]
    public void Unknown_interface_fails()
    {
        Assert.False(
            AvitoAgent.Infrastructure.Http.NetworkInterfaceResolver.TryResolve(
                "definitely-missing-adapter-xyz",
                out _,
                out var error
            )
        );
        Assert.Contains("не найден", error);
    }
}

public sealed class LmStudioErrorMapperTests
{
    [Fact]
    public void Connection_and_timeout_helpers()
    {
        Assert.True(
            LmStudioErrorMapper.IsConnectionFailure(
                new HttpRequestException("Connection refused to localhost")
            )
        );
        Assert.True(
            LmStudioErrorMapper.IsTimeout(
                new TaskCanceledException("canceled", new TimeoutException())
            )
        );
        Assert.True(LmStudioErrorMapper.IsTimeout(new TimeoutException()));
    }

    [Fact]
    public void TryParseContextOverflow()
    {
        var body =
            """{"error":"exceed_context_size_error","n_prompt_tokens":9000,"n_ctx":4096}""";
        Assert.True(LmStudioErrorMapper.TryParseContextOverflow(body, out var info));
        Assert.Equal(9000, info.PromptTokens);
        Assert.Equal(4096, info.ContextSize);
    }
}
