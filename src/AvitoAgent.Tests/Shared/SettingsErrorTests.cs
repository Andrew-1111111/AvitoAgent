using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Tests.Shared;

public sealed class SettingsErrorTests
{
    [Theory]
    [InlineData(null, "(пусто)")]
    [InlineData("", "(пусто)")]
    [InlineData("  ", "(пусто)")]
    [InlineData("abc", "abc")]
    [InlineData(42, "42")]
    public void FormatValue(object? value, string expected)
    {
        Assert.Equal(expected, SettingsError.FormatValue(value));
    }

    [Fact]
    public void Range_and_OneOf_contain_path()
    {
        Assert.Contains("Worker:Port", SettingsError.Range("Worker:Port", 0, 1, 10));
        Assert.Contains("A, B", SettingsError.OneOf("X", "z", ["A", "B"]));
    }

    [Fact]
    public void BindFailed_int_hint()
    {
        var text = SettingsError.BindFailed("Worker:MaxResults", "System.Int32");
        Assert.Contains("целое число", text);
    }

    [Fact]
    public void WorkerOptions_price_clamp_helpers()
    {
        var options = new WorkerOptions { MinPrice = -10, MaxPrice = -5 };
        Assert.Equal(0, options.GetMinPrice());
        Assert.Equal(0, options.GetMaxPrice());
    }

    [Fact]
    public void RequireRange_adds_failure()
    {
        var failures = new List<string>();
        SettingsError.RequireRange(failures, "X", 100, 0, 10);
        Assert.Single(failures);
        SettingsError.RequireRange(failures, "Y", 5, 0, 10);
        Assert.Single(failures);
    }

    [Fact]
    public void RequireHttpUrl_and_RequireOneOf()
    {
        var failures = new List<string>();
        SettingsError.RequireHttpUrl(failures, "Url", "ftp://x");
        SettingsError.RequireHttpUrl(failures, "Url", "https://avito.ru");
        SettingsError.RequireNotEmpty(failures, "Token", " ", "нужен токен");
        SettingsError.RequireOneOf(failures, "Sort", "нет", ["По дате"]);
        SettingsError.RequireOneOf(failures, "Sort", "По дате", ["По дате"]);
        Assert.Equal(3, failures.Count);
    }
}
