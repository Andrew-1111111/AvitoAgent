using AvitoAgent.Shared.Configuration;

namespace AvitoAgent.Tests.Shared;

public sealed class AvitoConditionTests
{
    [Theory]
    [InlineData("Все", AvitoConditionMode.All, "Все")]
    [InlineData("все товары", AvitoConditionMode.All, "Все")]
    [InlineData("Новое", AvitoConditionMode.New, "Новое")]
    [InlineData("новый", AvitoConditionMode.New, "Новое")]
    [InlineData("Б/у", AvitoConditionMode.Used, "Б/у")]
    [InlineData("бу", AvitoConditionMode.Used, "Б/у")]
    [InlineData("б.у.", AvitoConditionMode.Used, "Б/у")]
    [InlineData("подержанный", AvitoConditionMode.Used, "Б/у")]
    [InlineData("б\\у", AvitoConditionMode.Used, "Б/у")]
    public void TryResolve_aliases(string input, AvitoConditionMode mode, string canonical)
    {
        Assert.True(AvitoCondition.TryResolve(input, out var name, out var resolved));
        Assert.Equal(mode, resolved);
        Assert.Equal(canonical, name);
        Assert.True(AvitoCondition.IsAllowed(input));
        Assert.Equal(mode, AvitoCondition.Parse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("сломанный")]
    public void TryResolve_rejects_invalid(string? input)
    {
        Assert.False(AvitoCondition.TryResolve(input, out _, out _));
        Assert.False(AvitoCondition.IsAllowed(input));
    }

    [Fact]
    public void Parse_throws_for_invalid()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AvitoCondition.Parse("xxx"));
        Assert.Contains("Condition", ex.Message);
    }

    [Fact]
    public void DisplayName_covers_modes()
    {
        Assert.Equal("Все", AvitoCondition.DisplayName(AvitoConditionMode.All));
        Assert.Equal("Новое", AvitoCondition.DisplayName(AvitoConditionMode.New));
        Assert.Equal("Б/у", AvitoCondition.DisplayName(AvitoConditionMode.Used));
    }
}
