using System.Text.Json;
using AvitoAgent.Telegram.Services;

namespace AvitoAgent.Tests.Telegram;

public sealed class TelegramControlUiTests
{
    [Fact]
    public void ReplyKeyboardJson_contains_buttons_and_not_persistent()
    {
        var json = TelegramControlUi.ReplyKeyboardJson;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("is_persistent").GetBoolean());
        Assert.True(root.GetProperty("resize_keyboard").GetBoolean());

        var texts = root.GetProperty("keyboard")
            .EnumerateArray()
            .SelectMany(row => row.EnumerateArray())
            .Select(cell => cell.GetProperty("text").GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Старт", texts);
        Assert.Contains("Стоп", texts);
        Assert.Contains("Сон", texts);
        Assert.Contains("Авторизация", texts);
    }

    [Theory]
    [InlineData("sort:default")]
    [InlineData("sort:cheap")]
    [InlineData("sort:date")]
    public void SortKeyboard_callback_data(string expected)
    {
        Assert.Contains(expected, JsonSerializer.Serialize(TelegramControlUi.SortKeyboard()));
    }

    [Fact]
    public void Inline_keyboards_emit_expected_callbacks()
    {
        Assert.Contains("del:1", JsonSerializer.Serialize(TelegramControlUi.DeliveryKeyboard()));
        Assert.Contains("del:0", JsonSerializer.Serialize(TelegramControlUi.DeliveryKeyboard()));
        Assert.Contains("cond:new", JsonSerializer.Serialize(TelegramControlUi.ConditionKeyboard()));
        Assert.Contains("seller:priv", JsonSerializer.Serialize(TelegramControlUi.SellerKeyboard()));
        Assert.Contains("from:1", JsonSerializer.Serialize(TelegramControlUi.FromDateKeyboard()));
        Assert.Contains("auth:status", JsonSerializer.Serialize(TelegramControlUi.AvitoAuthKeyboard()));
    }
}
