using System.Text.Json;

namespace AvitoAgent.Telegram.Services;

internal static class TelegramControlUi
{
    public static readonly object ReplyKeyboard = new
    {
        keyboard = new object[]
        {
            new object[] { new { text = "Старт" }, new { text = "Стоп" }, new { text = "Статус" } },
            new object[] { new { text = "Запрос" }, new { text = "Присоединить к запросам" } },
            new object[] { new { text = "Исключения" }, new { text = "Регион" } },
            new object[]
            {
                new { text = "Цена" },
                new { text = "Сортировка" },
                new { text = "Доставка" },
            },
            new object[]
            {
                new { text = "Состояние" },
                new { text = "Продавец" },
                new { text = "Дата" },
            },
            new object[]
            {
                new { text = "Авторизация" },
                new { text = "Интервал" },
                new { text = "Сон" },
            },
        },
        resize_keyboard = true,
        // false: на iOS true держит клавиатуру постоянно на половину экрана.
        is_persistent = false,
    };

    public static string ReplyKeyboardJson => JsonSerializer.Serialize(ReplyKeyboard);

    public static object SortKeyboard() =>
        Inline(
            ("По умолчанию", "sort:default"),
            ("Дешевле", "sort:cheap"),
            ("Дороже", "sort:exp"),
            ("По дате", "sort:date"),
            ("По размеру скидки", "sort:discount")
        );

    public static object DeliveryKeyboard() =>
        Inline(("Только с доставкой", "del:1"), ("Любые", "del:0"));

    public static object ConditionKeyboard() =>
        Inline(("Все", "cond:all"), ("Новое", "cond:new"), ("Б/у", "cond:used"));

    public static object SellerKeyboard() =>
        Inline(("Все", "seller:all"), ("Частные", "seller:priv"), ("Компании", "seller:co"));

    public static object FromDateKeyboard() =>
        Inline(("С текущего момента", "from:1"), ("Все даты", "from:0"));

    public static object AvitoAuthKeyboard() => Inline(("Обновить статус", "auth:status"));

    private static object Inline(params (string Text, string Data)[] buttons) =>
        new
        {
            inline_keyboard = buttons
                .Select(button =>
                    new object[] { new { text = button.Text, callback_data = button.Data } }
                )
                .ToArray(),
        };
}
