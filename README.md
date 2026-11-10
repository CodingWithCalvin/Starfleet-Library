# Starfleet Library Uno

A personal Star Trek library with **native Uno Platform Windows and iPhone apps**, built with .NET 10.

This standalone project lives at `D:\coding-with-calvin\StarfleetLibraryUno`. All app code, core code, tests, assets and build configuration are included here. It has no project or build dependency on `StarfleetLibraryMaui`.

The public repository is [CodingWithCalvin/Starfleet-Library](https://github.com/CodingWithCalvin/Starfleet-Library). The Windows desktop application is in `src/StarfleetLibrary.Uno`; the native iPhone application is in `src/StarfleetLibrary.Mobile`. GitHub Actions builds both apps and tests the private sync service. Download the **Starfleet-Library-Windows** artifact from a successful **Windows desktop** run to get the desktop application.

## Run

Double-click **Launch Uno.cmd**. The launcher builds the Release app if it has not been built yet.

Open **StarfleetLibrary.Uno.slnx** for development. To rebuild after source changes, close the app and run:

```powershell
dotnet build src\StarfleetLibrary.Uno\StarfleetLibrary.Uno.csproj -c Release -f net10.0-windows10.0.26100
```

## Features

- Responsive cover gallery with corner ribbons identifying each edition’s format, wide cards and a scrollable table.
- Search and filters for series, reading status, format and books/comics, including COMIC and SNW labels.
- Owned books, wishlist, ISBN lookup and duplicate checks.
- Edition metadata, reading progress, ratings, finish dates, notes and insights.
- Local cover storage, offline image caching, JSON export and restore.
- Native iPhone Collection and Wishlist, Apple camera ISBN/UPC barcode scanning, and manual entry.
- End-to-end encrypted synchronization in your own Cloudflare account, with per-edition revision checks, offline changes and conflict review. No OneDrive or ChatGPT Sites dependency.

The desktop catalog stays at `%LOCALAPPDATA%\Starfleet Library Uno`. Connected devices share a private hosted catalog and cover images while retaining local copies. The MAUI project and its application data remain separate; Calibre is never modified.

## Checks

```powershell
dotnet run --project tests\StarfleetLibrary.Core.Tests\StarfleetLibrary.Core.Tests.csproj
dotnet run --project tests\StarfleetLibrary.Uno.Tests\StarfleetLibrary.Uno.Tests.csproj
dotnet run --project tests\StarfleetLibrary.Sync.Tests\StarfleetLibrary.Sync.Tests.csproj
dotnet run --project tests\StarfleetLibrary.PrivateSync.Tests\StarfleetLibrary.PrivateSync.Tests.csproj
npm test
```

See [setup, storage and backup details](docs/UNO-WINDOWS.md) and [validation notes](docs/UNO-VALIDATION.md).

See [native iPhone builds, pairing and private Cloudflare synchronization](docs/NATIVE-IOS.md).
