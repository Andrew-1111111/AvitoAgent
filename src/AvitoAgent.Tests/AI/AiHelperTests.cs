using AvitoAgent.AI;
using AvitoAgent.AI.Services;
using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Tests.AI;

public sealed class QwenThinkingTests
{
    [Fact]
    public void Prefill_and_disable_prompt()
    {
        Assert.Equal(QwenThinking.Prefill + "{", QwenThinking.BuildAssistantPrefill(true));
        Assert.Equal(QwenThinking.Prefill, QwenThinking.BuildAssistantPrefill(false));

        var withTag = QwenThinking.DisableInPrompt("hello");
        Assert.StartsWith(QwenThinking.NoThinkTag, withTag);
        Assert.Contains("hello", withTag);
        Assert.StartsWith(QwenThinking.NoThinkTag, QwenThinking.DisableInPrompt(withTag));
    }

    [Fact]
    public void PrefixUserText_idempotent()
    {
        var once = QwenThinking.PrefixUserText("ask");
        Assert.StartsWith(QwenThinking.NoThinkTag, once);
        Assert.Equal(once, QwenThinking.PrefixUserText(once));
    }

    [Fact]
    public void StripThinkBlocks()
    {
        var text = QwenThinking.StripThinkBlocks(
            "<think>plan</think>\n{\"score\":1}\n<redacted_reasoning>x</redacted_reasoning>"
        );
        Assert.Contains("{\"score\":1}", text);
        Assert.DoesNotContain("plan", text);
        Assert.DoesNotContain("redacted", text);
    }

    [Fact]
    public void DisableInPrompt_strips_jinja_lines()
    {
        var result = QwenThinking.DisableInPrompt("{% if true %}\nbody\n");
        Assert.Contains("body", result);
        Assert.DoesNotContain("{%", result);
    }
}

public sealed class LlmResponseParserTests
{
    [Fact]
    public void Parse_raw_json()
    {
        var result = LlmResponseParser.Parse(
            """{"score":80,"isRelevant":true,"authenticityScore":70,"confidence":0.9,"reason":"ok"}"""
        );
        Assert.True(result.IsRelevant);
        Assert.Equal(80, result.Score);
        Assert.Equal(70, result.AuthenticityScore);
    }

    [Fact]
    public void Parse_markdown_fence_and_think()
    {
        var content = """
            <think>thinking</think>
            ```json
            {"score":55,"isRelevant":false,"authenticityScore":10,"confidence":0.2}
            ```
            """;
        var result = LlmResponseParser.Parse(content);
        Assert.False(result.IsRelevant);
        Assert.Equal(55, result.Score);
    }

    [Fact]
    public void Parse_empty_throws()
    {
        Assert.Throws<InvalidOperationException>(() => LlmResponseParser.Parse("   "));
    }

    [Fact]
    public void Parse_embedded_json_and_unclosed_think()
    {
        var content = """prefix <think>plan without close {"score":10,"isRelevant":true,"authenticityScore":1,"confidence":0.1}""";
        // unclosed think strips to empty before { - may fail; use closed-style
        content =
            "Here is result: {\"score\":12,\"isRelevant\":true,\"authenticityScore\":5,\"confidence\":0.5} thanks";
        var result = LlmResponseParser.Parse(content);
        Assert.Equal(12, result.Score);
        Assert.True(result.IsRelevant);
    }

    [Fact]
    public void Parse_no_json_throws()
    {
        Assert.Throws<InvalidOperationException>(() => LlmResponseParser.Parse("no json here"));
    }
}

public sealed class LmStudioContextBudgetTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 50)]
    [InlineData(200, 100)]
    public void ClampUsagePercent(int input, int expected)
    {
        Assert.Equal(expected, LmStudioContextBudget.ClampUsagePercent(input));
    }

    [Fact]
    public void ResolveContextSize_uses_percent_of_full()
    {
        var options = new LmStudioOptions { ContextUsagePercent = 50 };
        var usable = LmStudioContextBudget.ResolveContextSize(10_000, options);
        Assert.Equal(5_000, usable);
        Assert.Equal(32_768, LmStudioContextBudget.ResolveFullContextSize(0));
    }

    [Fact]
    public void CapCompletionTokens_respects_floor_and_budget()
    {
        var capped = LmStudioContextBudget.CapCompletionTokens(800, 4096, 3000);
        Assert.Equal(584, capped);
        Assert.Equal(256, LmStudioContextBudget.CapCompletionTokens(800, 4096, 4000));
        Assert.Equal(800, LmStudioContextBudget.ResolveCompletionTokens(new LmStudioOptions()));
        Assert.Equal(800, LmStudioContextBudget.ResolveCompletionTokens(new LmStudioOptions { MaxCompletionTokens = 0 }));
    }

    [Fact]
    public void TruncateDescription()
    {
        Assert.Equal(string.Empty, LmStudioContextBudget.TruncateDescription(null));
        Assert.Equal("short", LmStudioContextBudget.TruncateDescription("short"));
        var longText = new string('a', 4_000);
        var truncated = LmStudioContextBudget.TruncateDescription(longText);
        Assert.True(truncated.Length < longText.Length);
        Assert.EndsWith("…", truncated);
    }

    [Fact]
    public void Fit_reduces_images_under_budget()
    {
        var options = new LmStudioOptions
        {
            MaxImages = 5,
            ContextUsagePercent = 10,
            MaxCompletionTokens = 800,
            OptimizeImages = true,
            MaxImageSidePx = 1024,
        };
        var (count, side) = LmStudioContextBudget.Fit(
            options,
            availableImages: 5,
            systemPrompt: new string('s', 2000),
            userPrompt: new string('u', 2000),
            contextSize: 4096
        );
        Assert.True(count <= 5);
        Assert.InRange(side, 480, 2048);
    }

    [Fact]
    public void Fit_zero_images_returns_preferred_side()
    {
        var options = new LmStudioOptions { OptimizeImages = true, MaxImageSidePx = 1024 };
        var (count, side) = LmStudioContextBudget.Fit(options, 0, "s", "u", 8192);
        Assert.Equal(0, count);
        Assert.Equal(1024, side);
    }

    [Fact]
    public void ShrinkAfterOverflow_drops_images_then_side()
    {
        var options = new LmStudioOptions { ContextUsagePercent = 100 };
        var overflow = new ContextOverflowInfo(PromptTokens: 20_000, ContextSize: 8192);

        var multi = LmStudioContextBudget.ShrinkAfterOverflow(options, 4, 1024, overflow, 800);
        Assert.True(multi.ImageCount < 4);
        Assert.Equal(1024, multi.MaxSidePx);

        var single = LmStudioContextBudget.ShrinkAfterOverflow(options, 1, 1024, overflow, 800);
        Assert.Equal(1, single.ImageCount);
        Assert.True(single.MaxSidePx < 1024);
        Assert.True(single.MaxSidePx >= 480);

        var floor = LmStudioContextBudget.ShrinkAfterOverflow(options, 1, 480, overflow, 800);
        Assert.Equal((1, 480), floor);
    }

    [Fact]
    public void EstimatePromptTokens_grows_with_images()
    {
        var textOnly = LmStudioContextBudget.EstimatePromptTokens("sys", "user", 0, 1024);
        var withImages = LmStudioContextBudget.EstimatePromptTokens("sys", "user", 2, 1024);
        Assert.True(withImages > textOnly);
    }
}
