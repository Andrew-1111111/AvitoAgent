# Avito Agent

[Русский](README.md) · [English](README.en.md)

A Windows console agent that watches Avito listings and sends new ones to Telegram. It drives the installed Google Chrome browser, types the query into the search box, applies filters, and remembers listings it has already seen. Optionally, listing text and photos can be scored by a local model through LM Studio.

Runs on Windows 10/11 x64. The browser is launched with Playwright using the `chrome` channel.

## Features

- Multiple search queries and regions, plus filters for price, condition, seller type, delivery, and sort order.
- Telegram controls: start and stop, query, exclusions, region, polling interval, and a night-time sleep window.
- Notifications with photos and the listing URL. A screenshot is sent when a captcha appears and when search results still fail to load after a page reload.
- Optional LM Studio analysis: relevance to the query and a score based on `prompts/authenticity.txt`.
- A persistent Chrome profile and an SQLite database of already seen listings under `data/`.

## Requirements

- 64-bit Windows 10 or 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build.
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) to run the **framework-dependent** build. The self-contained build does not need a separate runtime.
- Google Chrome.
- A Telegram bot (`BotToken` and `ChatId`) if you want notifications and chat controls.
- [LM Studio](https://lmstudio.ai/) only when `LmStudio:Enabled` is set.

## Quick start

```powershell
copy src\AvitoAgent.App\appsettings.example.json src\AvitoAgent.App\appsettings.json
# Fill in Telegram:BotToken, Telegram:ChatId, and Avito:Auth if needed
dotnet run --project src\AvitoAgent.App\AvitoAgent.App.csproj -c Release
```

`appsettings.json` is gitignored: it holds bot tokens and Avito login details.

The `prompts/` folder must sit next to the published app or somewhere above the executable. The agent looks for `prompts/authenticity.txt`.

## Build

**Framework-dependent** - the machine needs the .NET 10 Runtime:

```powershell
dotnet publish src\AvitoAgent.App\AvitoAgent.App.csproj `
  -c Release -r win-x64 --self-contained false `
  -o .\publish\framework-dependent
Copy-Item -Recurse prompts .\publish\framework-dependent\prompts
```

**Self-contained** - the runtime is bundled; Chrome is still required:

```powershell
dotnet publish src\AvitoAgent.App\AvitoAgent.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\publish\self-contained
Copy-Item -Recurse prompts .\publish\self-contained\prompts
```

Run `AvitoAgent.App.exe` from the publish folder.

## Tests

```powershell
dotnet test src\AvitoAgent.Tests\AvitoAgent.Tests.csproj -c Release
```

Unit coverage (no browser): Avito filters and URLs, geo catalog, date/image parsing, sleep schedule, LM Studio helpers/errors/budget, options validators, Telegram parse/format/UI, SQLite repository, tracker blocklist, network errors, `ParseSession`.

## GitHub Actions

`.github/workflows/build.yml` runs unit tests first, then builds both `win-x64` variants on every push and pull request and uploads artifacts:

| Artifact | Contents |
| --- | --- |
| `AvitoAgent-win-x64-framework-dependent` | Publish output without the runtime; needs .NET 10 |
| `AvitoAgent-win-x64-self-contained` | Publish output with the runtime bundled |

Artifacts ship `appsettings.example.json` as `appsettings.json` (no secrets). Fill in tokens before running a downloaded build.

### Release

`.github/workflows/release.yml` runs on git tags matching `v*` (for example `v1.0.0`): tests, builds both zips, and publishes a [GitHub Release](../../releases) with them.

```powershell
git tag v1.0.0
git push origin v1.0.0
```

## Repository layout

```
src/AvitoAgent.App            entry point
src/AvitoAgent.Worker         search cycles and sleep window
src/AvitoAgent.Avito          Avito navigation and page parsing
src/AvitoAgent.Playwright     browser, automation masking, networking
src/AvitoAgent.Telegram       notifications and control keyboard
src/AvitoAgent.AI             LM Studio client
src/AvitoAgent.Storage        SQLite store
src/AvitoAgent.Tests          unit tests (xUnit)
prompts/authenticity.txt      listing analysis prompt
```

## Telegram controls

Keyboard: Start, Stop, Status, Query, Append to queries, Exclusions, Region, Price, Sort, Delivery, Condition, Seller, Date, Authorization, Interval, Sleep.

Initial values come from the `Worker` and `Avito:Filters` sections in `appsettings.json`. After that they can be changed from the chat without restarting the app.

## Tech stack

- **C# / .NET 10** - Generic Host, DI, Options, `IHttpClientFactory`
- **Microsoft.Playwright** - driving the installed Google Chrome browser
- **Telegram Bot API** - notifications and the control keyboard over HTTP
- **LM Studio** - local OpenAI-compatible API for listing analysis
- **SQLite** (`Microsoft.Data.Sqlite`) - already seen listings
- **SkiaSharp** - image preparation for the model
- **Serilog** - console and file logging
- **Polly** (`Microsoft.Extensions.Http.Resilience`) - HTTP retries
- **GitHub Actions** - unit tests, win-x64 builds, release on `v*` tags
- **xUnit** - unit tests in `src/AvitoAgent.Tests`
