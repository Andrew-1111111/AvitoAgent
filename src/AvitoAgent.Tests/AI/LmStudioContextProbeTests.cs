using System.Text.Json;
using AvitoAgent.AI.Services;

namespace AvitoAgent.Tests.AI;

public sealed class LmStudioContextProbeTests
{
    [Fact]
    public void HugeContextWarnThreshold_is_32k()
    {
        Assert.Equal(32_768, LmStudioContextProbe.HugeContextWarnThreshold);
    }

    [Fact]
    public void ReadContextInfo_matches_by_id_and_prefers_loaded_ctx()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "data": [
                {
                  "id": "qwen/qwen3-vl",
                  "state": "loaded",
                  "loaded_context_length": 8192,
                  "max_context_length": 131072
                }
              ]
            }
            """
        );

        var info = LmStudioContextProbe.ReadContextInfo(doc.RootElement, "qwen3-vl");
        Assert.NotNull(info);
        Assert.True(info!.Value.IsLoaded);
        Assert.Equal(8192, info.Value.LoadedContextLength);
        Assert.Equal(131072, info.Value.MaxContextLength);
        Assert.Equal(8192, info.Value.AvailableContextLength);
        Assert.Equal(8192, LmStudioContextProbe.ReadContextLength(doc.RootElement, "qwen3-vl"));
    }

    [Fact]
    public void ReadContextInfo_models_array_and_loaded_instances()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "models": [
                {
                  "key": "local-model",
                  "loaded_instances": [
                    { "config": { "n_ctx": 4096 } }
                  ],
                  "max_model_len": 32000
                }
              ]
            }
            """
        );

        var info = LmStudioContextProbe.ReadContextInfo(doc.RootElement, "local-model");
        Assert.NotNull(info);
        Assert.True(info!.Value.IsLoaded);
        Assert.Equal(4096, info.Value.AvailableContextLength);
    }

    [Fact]
    public void ReadContextInfo_falls_back_to_any_loaded_when_id_missing()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "data": [
                { "id": "other", "state": "loaded", "loaded_context_length": 2048 }
              ]
            }
            """
        );

        var info = LmStudioContextProbe.ReadContextInfo(doc.RootElement, "unknown-model");
        Assert.NotNull(info);
        Assert.Equal(2048, info!.Value.AvailableContextLength);
    }

    [Fact]
    public void ReadContextInfo_uses_max_when_not_loaded()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "data": [
                { "id": "cold", "state": "not-loaded", "max_context_length": 16000 }
              ]
            }
            """
        );

        var info = LmStudioContextProbe.ReadContextInfo(doc.RootElement, "cold");
        Assert.NotNull(info);
        Assert.False(info!.Value.IsLoaded);
        Assert.Equal(16000, info.Value.AvailableContextLength);
    }

    [Fact]
    public void ReadContextInfo_null_when_empty()
    {
        using var doc = JsonDocument.Parse("""{"data":[]}""");
        Assert.Null(LmStudioContextProbe.ReadContextInfo(doc.RootElement, "x"));
        Assert.Null(LmStudioContextProbe.ReadContextLength(doc.RootElement, "x"));
    }
}
