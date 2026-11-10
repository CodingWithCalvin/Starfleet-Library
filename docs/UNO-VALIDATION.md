# Uno Windows validation — November 9, 2026

## Cover format ribbons

- Added diagonal upper-right format ribbons to Covers view: teal E-BOOK, brown PAPERBACK, blue HARDCOVER and purple AUDIOBOOK. Labels are bound to each edition; the overlay does not intercept cover clicks.
- Release build passed with 0 warnings and 0 errors. Visually checked the existing paperback and e-book copies of Vendetta at 1360 × 900 and 680 × 760, opened a labeled cover into details, and verified the ribbons after scrolling into recycled gallery tiles.
- Screenshots: `work/desktop-layout/format-ribbons-duplicates.png`, `ribbon-narrow.png` and `ribbon-recycled.png`. No catalog records were changed.

## Follow-up: standalone project, cover fix and Calibre update

- Moved the Uno app, solution, launcher, tests and docs to `D:\coding-with-calvin\StarfleetLibraryUno`. Copied the core code and its tests so this project has no build dependency on the MAUI folder. The MAUI app and its core code remain in their original folder.
- Built the complete `StarfleetLibrary.Uno.slnx` solution from the new folder in Release: **0 warnings, 0 errors**. Both test suites passed from the new folder: **20 core + 4 Uno storage checks**.
- Reproduced missing covers by scrolling to 70% of the gallery: most recycled tiles showed placeholders despite their image files being present. Fixed image loading on `DataContextChanged` as well as `Loaded`, so recycled image controls request artwork for their new book. A failed fetch can now retry on a later visit instead of permanently marking that book as already requested.
- Launched the new executable and visually verified artwork at the middle, 70% and end of the collection. The previously blank rows now display images. Screenshots are in `work/desktop-layout/covers-after-*.png`; the pre-fix screenshot is `work/covers-before.png`.
- Imported exactly five newly added Calibre records (605–609): The Lives of Dax, The Fall of Terok Nor, The Buried Age, Mission Gamma: Book Four and Voyager: Echoes. Their series were confirmed from the titles/local title-page images. All five were imported as unread books, with their Calibre metadata and local images.
- Catalog now contains **228 editions: 135 books, 93 comics, 194 e-books and 34 physical editions**. All 223 prior records, including eight finished reading logs, were verified unchanged. Calibre's database SHA-256 remained unchanged during extraction and import.
- All 228 entries have cover files available: **194 imported images** (decoded successfully during audit) and **34 cached images**. Some Calibre images are title pages rather than illustrated cover art; those are retained as supplied.
- Pre-import backup: `%LOCALAPPDATA%\Starfleet Library Uno\Backups\catalog-before-calibre-20261109-175533.json`. The normal Uno AppData path was retained across the project move.

The sections below record the original implementation checks before this follow-up.

## Build and automated checks

- Windows Release build: **passed, 0 warnings, 0 errors** using .NET SDK 10.0.403, Uno.Sdk 6.7.45 and the native WinUI Windows target `net10.0-windows10.0.26100`, `win-x64`.
- Initial Debug implementation also built and ran successfully. The final user-facing app is the Release executable.
- Shared core suite: **20/20 passed**.
- Uno storage suite: **4/4 passed**, including first-run copy and source preservation, no reseeding on restart or after intentionally clearing, corrupt catalog protection, failed source imports, concurrent saves, defensive snapshots, invalid restores and pre-restore backups.
- Existing project structural validator: passed. The MAUI app and shared core implementation were not changed for the Uno edition.

## Native Windows checks

The actual WinUI windows were operated through Windows UI Automation and inspected in screenshots. Test additions, edits and deletions used `work\uno-qa` as the data directory.

- Imported 223 records: 130 books, 93 comics, 189 e-books and 34 physical editions. Preserved the eight finished reading records and all 189 imported local covers.
- Cover-only view: six columns at 1360 × 900; three columns at 680 × 760. Sidebar labels collapse at narrower widths. Artwork remains clickable and has accessible title/author names.
- Table and wide-card views rendered correctly. The table identifies the SNW comic with separate `SNW` and `COMIC` columns; narrow tables scroll horizontally.
- Book/comic filters produce 130 and 93 records respectively. Searching SNW finds one comic or two books, depending on type. The series filter independently shows the two SNW books and resets successfully.
- Opened a cover into details. Persistent sidebar and the large back button remained available. The edit dialog reflowed when resized from 1360 × 900 to 680 × 760; Cancel preserved the edition.
- Empty-title validation prevented a save. Added a temporary edition, changed its title, marked it finished, and removed it after confirmation. The test library returned to 223 records with eight finished entries and no temporary editions.
- ISBN `9780671795672` matched the existing Requiem paperback. A separate add form with that ISBN warned about the existing edition and required the explicit second save; cancel left the catalog unchanged.
- Online Open Library lookup for `9780306406157` returned metadata and a review/add action. It was not added to the catalog.
- Insights showed the expected totals, formats, series distribution and reading history.
- Native Save As and Open backup dialogs opened. Full automated picker export/restore was not completed; picker automation could not reliably address the brokered filename controls. Catalog validation, replacement and preservation of the pre-restore backup passed the storage suite.
- Final Release launch used the normal `%LOCALAPPDATA%\Starfleet Library Uno` directory. A second launch exited and reused the first window; exactly one Uno process remained.

## Source preservation and final state

The MAUI catalog's SHA-256 remained unchanged:

```text
CE57F4473C66C525BF31E25D6BDE27AFBCBC8CC44B5FEFA61CB175C18E23A9EC
```

The final Uno library has 223 records and 189 imported local cover files. No Calibre files were read or changed during this migration; the copy came from the existing MAUI catalog and cover directories.

The Release app was left open at 1360 × 900 in Covers view, Books selected, search and extra filters cleared. Native screenshots and UI trees are under `work\desktop-layout\uno-*.png/json`, including `uno-final-wide`, `uno-final-narrow`, `uno-final-edit-narrow`, `uno-table-wide`, `uno-cards` and `uno-insights`.

This is a locally built, unpackaged Windows app. MSIX signing, installer distribution and other Windows machines were not tested.
