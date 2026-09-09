using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AvitoAgent.AI.Services;
using AvitoAgent.Core;

namespace AvitoAgent.Tests.AI;

public sealed class LmStudioErrorMapperExtendedTests
{
    private const string BaseUrl = "http://127.0.0.1:1234/v1/";

    [Fact]
    public void Factory_messages_include_url_and_model_list()
    {
        Assert.Contains("127.0.0.1", LmStudioErrorMapper.NotRunning(BaseUrl).Message);
        Assert.Contains("не загружена", LmStudioErrorMapper.NoModelLoaded().Message);
        Assert.Contains("qwen", LmStudioErrorMapper.ModelNotFound("qwen", []).Message);
        Assert.Contains(
            "alpha",
            LmStudioErrorMapper.ModelNotFound("qwen", ["alpha", "beta"]).Message
        );
        Assert.Contains("не отвечает", LmStudioErrorMapper.NotResponding(BaseUrl).Message);
        Assert.Contains("оборвала ответ", LmStudioErrorMapper.ResponseEnded(BaseUrl).Message);
    }

    [Theory]
    [InlineData("""{"error":"invalid image url"}""", "изображение")]
    [InlineData("""{"error":"engine protocol predict request failed"}""", "перезагрузите модель")]
    [InlineData("""{"error":"empty grammar stack"}""", "грамматику")]
    public void FromHttpError_special_bodies(string body, string expectedFragment)
    {
        var ex = LmStudioErrorMapper.FromHttpError(500, body);
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains(expectedFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromHttpError_context_overflow()
    {
        var body =
            """{"error":"exceed_context_size_error","n_prompt_tokens":12000,"n_ctx":8192}""";
        var ex = Assert.IsType<InvalidOperationException>(
            LmStudioErrorMapper.FromHttpError(400, body)
        );
        Assert.Contains("12000", ex.Message);
        Assert.Contains("8192", ex.Message);
    }

    [Fact]
    public void FromHttpError_400_generic_and_missing_model()
    {
        var generic = LmStudioErrorMapper.FromHttpError(400, "bad request details");
        Assert.IsType<InvalidOperationException>(generic);
        Assert.Contains("HTTP 400", generic.Message);

        var missing = Assert.IsType<LmStudioUnavailableException>(
            LmStudioErrorMapper.FromHttpError(404, "model not found")
        );
        Assert.Contains("не нашла", missing.Message);
    }

    [Fact]
    public void RequestFailed_load_failure()
    {
        var ex = LmStudioErrorMapper.RequestFailed(503, "failed to load model: oom");
        Assert.Contains("загрузить модель", ex.Message);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("ok", false)]
    [InlineData("invalid image", true)]
    [InlineData("predict request failed", true)]
    [InlineData("empty grammar stack", true)]
    [InlineData("model_not_found", true)]
    public void LooksLike_helpers(string? body, bool anyTrue)
    {
        var hit =
            LmStudioErrorMapper.LooksLikeInvalidImageUrl(body)
            || LmStudioErrorMapper.LooksLikeEnginePredictFailure(body)
            || LmStudioErrorMapper.LooksLikeGrammarError(body)
            || LmStudioErrorMapper.LooksLikeMissingModel(body);
        Assert.Equal(anyTrue, hit);
    }

    [Fact]
    public void LooksLikeLoadFailure_empty_is_true()
    {
        Assert.True(LmStudioErrorMapper.LooksLikeLoadFailure(null));
        Assert.True(LmStudioErrorMapper.LooksLikeLoadFailure(""));
        Assert.False(LmStudioErrorMapper.LooksLikeLoadFailure("unrelated"));
    }

    [Fact]
    public void TryMap_connection_timeout_json_and_passthrough()
    {
        var original = LmStudioErrorMapper.NotRunning(BaseUrl);
        Assert.Same(original, LmStudioErrorMapper.TryMap(original, BaseUrl));

        var mapped = LmStudioErrorMapper.TryMap(
            new HttpRequestException("Connection refused", new SocketException()),
            BaseUrl
        );
        Assert.NotNull(mapped);
        Assert.Contains("не запущена", mapped!.Message);

        var timeout = LmStudioErrorMapper.TryMap(new TimeoutException(), BaseUrl);
        Assert.NotNull(timeout);
        Assert.Contains("не отвечает", timeout!.Message);

        var json = LmStudioErrorMapper.TryMap(new JsonException("bad"), BaseUrl);
        Assert.NotNull(json);
        Assert.Contains("JSON", json!.Message);

        var http = LmStudioErrorMapper.TryMap(
            new HttpRequestException("fail", null, HttpStatusCode.BadGateway),
            BaseUrl
        );
        Assert.NotNull(http);
    }

    [Fact]
    public void TryMap_premature_returns_null_but_analysis_maps()
    {
        var premature = new IOException("The response ended prematurely");
        Assert.Null(LmStudioErrorMapper.TryMap(premature, BaseUrl));
        var analysis = LmStudioErrorMapper.TryMapAnalysisError(premature, BaseUrl);
        Assert.IsType<InvalidOperationException>(analysis);
        Assert.Contains("оборвала ответ", analysis!.Message);
    }

    [Fact]
    public void IsPrematureResponse_and_connection_message()
    {
        Assert.True(
            LmStudioErrorMapper.IsPrematureResponse(
                new InvalidOperationException("connection was closed")
            )
        );
        Assert.True(
            LmStudioErrorMapper.IsConnectionFailure(
                new InvalidOperationException("network is unreachable")
            )
        );
        Assert.False(LmStudioErrorMapper.IsPrematureResponse(new InvalidOperationException("ok")));
    }

    [Fact]
    public void TryParseContextOverflow_alternate_phrases()
    {
        Assert.True(
            LmStudioErrorMapper.TryParseContextOverflow(
                "request (5000 tokens) exceeds the available context size (2048 tokens)",
                out var info
            )
        );
        Assert.Equal(5000, info.PromptTokens);
        Assert.Equal(2048, info.ContextSize);
        Assert.False(LmStudioErrorMapper.TryParseContextOverflow("ok", out _));
    }
}
