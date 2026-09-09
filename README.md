# Avito Agent

[Русский](README.md) · [English](README.en.md)

Консольное приложение для Windows, которое ищет новые объявления на Avito и присылает их в Telegram. Агент открывает сайт в установленном Google Chrome, вводит запрос в строку поиска, применяет фильтры и запоминает уже просмотренные карточки. По желанию текст и фото можно отправить в локальную модель через LM Studio.

Работает на Windows 10/11 x64. Браузер запускается через Playwright с каналом `chrome`.

## Возможности

- Несколько поисковых запросов и регионов, фильтры по цене, состоянию, типу продавца, доставке и сортировке.
- Управление из Telegram: старт и стоп, запрос, исключения, регион, интервал опроса, ночной перерыв.
- Уведомления с фотографиями и ссылкой на объявление. При капче и при таймауте выдачи (если карточки не появились даже после перезагрузки страницы) в чат уходит скриншот.
- Необязательный разбор объявления в LM Studio: релевантность запросу и оценка по промпту `prompts/authenticity.txt`.
- Постоянный профиль Chrome и база SQLite уже виденных объявлений в каталоге `data/`.

## Требования

- Windows 10 или 11, 64-бит.
- Для сборки - [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Для запуска сборки **с зависимостью от .NET** - [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Автономная сборка runtime не требует.
- Google Chrome.
- Telegram-бот (`BotToken` и `ChatId`), если нужны уведомления и управление из чата.
- [LM Studio](https://lmstudio.ai/) - только при `LmStudio:Enabled`.

## Быстрый старт

```powershell
copy src\AvitoAgent.App\appsettings.example.json src\AvitoAgent.App\appsettings.json
# Укажите Telegram:BotToken, Telegram:ChatId и при необходимости Avito:Auth
dotnet run --project src\AvitoAgent.App\AvitoAgent.App.csproj -c Release
```

Файл `appsettings.json` в git не попадает: в нём токены бота и данные входа на Avito.

Каталог `prompts/` должен лежать рядом с опубликованным приложением или выше по дереву папок от exe. Агент ищет файл `prompts/authenticity.txt`.

## Сборка

**С зависимостью от .NET** - на компьютере должен быть установлен .NET 10 Runtime:

```powershell
dotnet publish src\AvitoAgent.App\AvitoAgent.App.csproj `
  -c Release -r win-x64 --self-contained false `
  -o .\publish\framework-dependent
Copy-Item -Recurse prompts .\publish\framework-dependent\prompts
```

**Автономная (self-contained)** - среда выполнения входит в сборку, достаточно Chrome:

```powershell
dotnet publish src\AvitoAgent.App\AvitoAgent.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\publish\self-contained
Copy-Item -Recurse prompts .\publish\self-contained\prompts
```

Запуск: `AvitoAgent.App.exe` из каталога публикации.

## Тесты

```powershell
dotnet test src\AvitoAgent.Tests\AvitoAgent.Tests.csproj -c Release
```

Покрыты unit-тестами (без браузера): фильтры и URL Avito, геосправочник, разбор дат и картинок, сон агента, LM Studio helpers/ошибки/бюджет, валидаторы опций, Telegram parse/format/UI, SQLite-репозиторий, блоклист трекеров, ошибки сети, `ParseSession`.

## Сборка в GitHub Actions

Файл `.github/workflows/build.yml` сначала прогоняет unit-тесты, затем на каждый push и pull request собирает оба варианта под `win-x64` и публикует артефакты:

| Артефакт | Содержимое |
| --- | --- |
| `AvitoAgent-win-x64-framework-dependent` | Сборка без runtime, нужен .NET 10 |
| `AvitoAgent-win-x64-self-contained` | Сборка вместе с runtime |

В артефактах вместо секретов лежит `appsettings.example.json` (как `appsettings.json`). Перед запуском скачанной сборки заполните токены.

## Структура репозитория

```
src/AvitoAgent.App            точка входа
src/AvitoAgent.Worker         циклы поиска и ночной перерыв
src/AvitoAgent.Avito          навигация по Avito и разбор страниц
src/AvitoAgent.Playwright     браузер, маскировка автоматизации, сеть
src/AvitoAgent.Telegram       уведомления и клавиатура управления
src/AvitoAgent.AI             клиент LM Studio
src/AvitoAgent.Storage        база SQLite
src/AvitoAgent.Tests          unit-тесты (xUnit)
prompts/authenticity.txt      промпт для анализа объявления
```

## Управление в Telegram

Клавиатура: Старт, Стоп, Статус, Запрос, Присоединить к запросам, Исключения, Регион, Цена, Сортировка, Доставка, Состояние, Продавец, Дата, Авторизация, Интервал, Сон.

Начальные значения берутся из секций `Worker` и `Avito:Filters` в `appsettings.json`. Дальше их можно менять из чата без перезапуска программы.

## Стек технологий

- **C# / .NET 10** - Generic Host, DI, Options, `IHttpClientFactory`
- **Microsoft.Playwright** - управление установленным Google Chrome
- **Telegram Bot API** - уведомления и клавиатура управления (HTTP)
- **LM Studio** - локальный OpenAI-совместимый API для разбора объявлений
- **SQLite** (`Microsoft.Data.Sqlite`) - уже виденные объявления
- **SkiaSharp** - подготовка фотографий для модели
- **Serilog** - консоль и файлы логов
- **Polly** (`Microsoft.Extensions.Http.Resilience`) - повторы HTTP-запросов
- **GitHub Actions** - unit-тесты, сборка win-x64 с зависимостью от .NET и автономная
- **xUnit** - unit-тесты в `src/AvitoAgent.Tests`
