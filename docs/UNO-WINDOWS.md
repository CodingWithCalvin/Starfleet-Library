# Starfleet Library for Windows — Uno edition

This is a standalone Windows-only app at `D:\coding-with-calvin\StarfleetLibraryUno`, built with **Uno.Sdk 6.7.45**, .NET 10 and native WinUI 3. It includes its own core code and tests, has no MAUI project dependency, and has no Android, iOS, browser or Skia Desktop target. The existing MAUI app remains in its original project. Moving this source project does not move or reset the Uno data in AppData.

## Open and run

Double-click **Launch Uno.cmd** in the repository root. It opens the Release app, building it first if needed. After changing source, rebuild using the command below; the launcher does not rebuild an existing executable automatically.

For development, open **StarfleetLibrary.Uno.slnx**, or run from the repository root:

```powershell
dotnet build src\StarfleetLibrary.Uno\StarfleetLibrary.Uno.csproj -c Release -f net10.0-windows10.0.26100
dotnet run --project src\StarfleetLibrary.Uno\StarfleetLibrary.Uno.csproj -c Release -f net10.0-windows10.0.26100 --no-build
```

Use .NET 10 SDK on Windows with its Windows build tools. The Uno SDK and Windows App SDK dependencies restore from NuGet. The generated template package is not required to build. The app is unpackaged, x64, and includes its .NET and Windows App SDK runtime dependencies. Close the running app before rebuilding its configuration.

## The new interface

- A persistent sidebar provides Collection, Reading now, Wishlist, Find by ISBN, Insights and Settings. It collapses to labeled tooltips on narrower windows. Book details include a large back button.
- Cover view displays artwork with a small, color-coded format ribbon in the upper-right corner: E-BOOK, PAPERBACK, HARDCOVER or AUDIOBOOK. Titles are available to screen readers and on hover. The native gallery reflows with the available width. Wide cards and a horizontally scrollable table are also available.
- Search titles, authors, ISBNs and series. Choose All / Books / Comics, then refine by series, format, reading status and sort order. The layout and type choice are remembered.
- Add and edit all edition metadata, covers, notes, reading status, page counts, ratings and an optional finish date. ISBN checks warn before saving duplicate editions; separate copies are allowed after acknowledgment.
- ISBN lookup checks existing editions and can retrieve metadata from Open Library. Imported local covers and downloaded artwork work offline once stored.
- Insights show owned editions, finished stories, current reads, wishlist totals, series distribution and the reading log.

## Independent local data

On the first launch only, the app reads the MAUI catalog at:

```text
%LOCALAPPDATA%\Starfleet Library\com.example.starfleetlibrary\Data\StarfleetLibrary\catalog.json
```

It validates that catalog, copies imported covers and the image cache, then commits a separate catalog under:

```text
%LOCALAPPDATA%\Starfleet Library Uno\
  Data\catalog.json
  Data\covers\
  Cache\covers\
  Backups\
  preferences.json
```

Existing Uno data is never automatically replaced. Changes do **not** sync between MAUI and Uno. Neither the MAUI library nor Calibre is modified. Missing source data starts an empty library; invalid source or destination data opens read only and preserves the files.

The app allows one running window per data directory. Launching it again activates the existing window, preventing separate in-memory catalogs from overwriting each other's changes.

Settings exports and restores the same validated JSON format as MAUI. Restore and clear operations ask for confirmation and save a timestamped copy of the previous catalog first. JSON includes metadata and cover URLs, but not local image files. To preserve imported artwork when moving to another PC, copy the entire Uno data folder while the app is closed.

For isolated UI testing, set `STARFLEET_UNO_DATA_DIR` to a separate directory before launching. This changes only the destination; first launch can still seed from the read-only MAUI source. QA runs use `work\uno-qa`, not the normal user library.

## Verification

```powershell
dotnet run --project tests\StarfleetLibrary.Core.Tests\StarfleetLibrary.Core.Tests.csproj
dotnet run --project tests\StarfleetLibrary.Uno.Tests\StarfleetLibrary.Uno.Tests.csproj
```

The shared suite covers 20 validation, backup, persistence, classification and cover scenarios. Four additional Uno storage tests cover first-run migration, source preservation, repeat launches, deliberately empty libraries, failed imports, corrupt catalogs, concurrent writes, defensive snapshots and restore backups. Native UI verification is recorded in `docs/UNO-VALIDATION.md`.

Implementation references: [Uno template options](https://platform.uno/docs/articles/getting-started/wizard/using-wizard.html) and [Windows App SDK lifecycle](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/guides/applifecycle).
