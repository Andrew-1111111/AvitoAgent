# Avito Agent

Windows-агент поиска объявлений на Avito: ходит по выдаче как человек, запоминает уже виденные карточки и присылает новые в Telegram. По желанию прогоняет фото и текст через локальную модель в LM Studio.

Целевая платформа — Windows x64. Браузер — установленный Google Chrome (Playwright, канал `chrome`).

## Возможности

- Несколько поисковых запросов, регионов и фильтров (цена, состояние, продавец, доставка, сортировка).
- Управление из Telegram: старт/стоп, запрос, исключения, регион, интервал, окно сна.
- Уведомления с фото и ссылкой на объявление; скриншот при капче и при таймауте выдачи (после перезагрузки страницы).
- Опциональный разбор объявления в LM Studio (релевантность / подлинность по `prompts/authenticity.txt`).
- Постоянный профиль Chrome и SQLite-база уже виденных объявлений в `data/`.

## Требования

- Windows 10/11 x64.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) для сборки. Для запуска framework-dependent сборки нужен [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0); self-contained runtime не требует.
- Google Chrome.
- Telegram-бот (`BotToken` + `ChatId`) — если нужны уведомления и управление из чата.
- [LM Studio](https://lmstudio.ai/) — только если включён `LmStudio:Enabled`.

## Быстрый старт

```powershell
copy src\AvitoAgent.App\appsettings.example.json src\AvitoAgent.App\appsettings.json
# Заполните Telegram:BotToken, Telegram:ChatId и при необходимости Avito:Auth
dotnet run --project src\AvitoAgent.App\AvitoAgent.App.csproj -c Release
```

`appsettings.json` в git не попадает: там токены и пароль аккаунта Avito.

Каталог `prompts/` должен быть рядом с опубликованным приложением или выше по дереву от exe — агент ищет `prompts/authenticity.txt`.

## Сборка

Framework-dependent (нужен установленный .NET 10 Runtime):

```powershell
dotnet publish src\AvitoAgent.App\AvitoAgent.App.csproj `
  -c Release -r win-x64 --self-contained false `
  -o .\publish\framework-dependent
Copy-Item -Recurse prompts .\publish\framework-dependent\prompts
```

Self-contained (runtime внутри сборки, ставится только Chrome):

```powershell
dotnet publish src\AvitoAgent.App\AvitoAgent.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\publish\self-contained
Copy-Item -Recurse prompts .\publish\self-contained\prompts
```

Запуск: `AvitoAgent.App.exe` из каталога публикации.

## CI

GitHub Actions (`.github/workflows/build.yml`) на каждый push и pull request собирает оба варианта под `win-x64` и выкладывает артефакты:

| Артефакт | Что внутри |
| --- | --- |
| `AvitoAgent-win-x64-framework-dependent` | Публикация без runtime, нужен .NET 10 |
| `AvitoAgent-win-x64-self-contained` | Публикация с runtime |

В артефакты кладётся `appsettings.example.json` как `appsettings.json` — без секретов. Перед запуском скачанной сборки заполните токены.

## Структура

```
src/AvitoAgent.App            точка входа
src/AvitoAgent.Worker         циклы поиска и сон
src/AvitoAgent.Avito          навигация и разбор Avito
src/AvitoAgent.Playwright     браузер, stealth, привязка сети
src/AvitoAgent.Telegram       уведомления и клавиатура управления
src/AvitoAgent.AI             LM Studio
src/AvitoAgent.Storage        SQLite
prompts/authenticity.txt      системный промпт анализа
```

## Управление в Telegram

Клавиатура: Старт, Стоп, Статус, Запрос, Присоединить к запросам, Исключения, Регион, Цена, Сортировка, Доставка, Состояние, Продавец, Дата, Авторизация, Интервал, Сон.

Начальные значения берутся из `Worker` и `Avito:Filters` в `appsettings.json`, дальше их можно менять из чата без перезапуска.
