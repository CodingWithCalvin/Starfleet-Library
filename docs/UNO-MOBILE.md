# Uno mobile companion

The mobile app is an actual Uno C#/XAML WebAssembly application in `src/StarfleetLibrary.Mobile`, optimized for an iPhone Home Screen installation. Its two tabs are **Collection** and **Wishlist**. It shares the edition model and synchronization client with the Windows app, while retaining a separate mobile interface.

## On iPhone

Open [Starfleet Library](https://starfleet-library-calvin.speedykazoo2.chatgpt.site) in Safari, sign in with the owning ChatGPT account, then use **Share → Add to Home Screen**. Let the first online sync finish before shopping. Catalog data is saved on the phone; the app shell and hosted cover images are cached for offline use. Browser/OS storage clearing can remove the local copy, so reopen online to download it again.

Search checks titles, authors, ISBNs and series labels. Filters include books/comics and edition formats. Cover view preserves the colored format ribbons. Wishlist entries have **I bought this → Move to Collection**. Editing an edition preserves desktop-only reading metadata.

## Barcode scanner

Tap **Scan ISBN / barcode**, then allow camera access when Safari asks. The scanner prefers the rear camera. Keep the complete barcode visible and in focus. A photo of a barcode and manual ISBN entry are available if camera access is unavailable.

- ISBN-13 (978/979 EAN-13) scans first check both Collection and Wishlist, including equivalent ISBN-10 values.
- Existing editions are shown with their format and ownership; opening a wishlist match offers the purchase action.
- New ISBNs can be looked up using Open Library, reviewed and saved. Lookup requires a network connection; catalog matching and manual entry work offline.
- UPC/EAN retail barcodes can be decoded, but an arbitrary retail UPC cannot reliably be converted to an ISBN. The app explains this and directs the user to the book's ISBN barcode or a title search.
- Camera frames and selected barcode photos are decoded locally, not uploaded. Camera tracks stop on completion, cancellation and when the app is hidden.

## Private storage and sync

The Sites project `appgprj_6af2201178b48191a618d1de9eca7575` uses owner-only access. Sites provides authentication, a Cloudflare D1 catalog database (`DB`) and R2 cover storage (`BUCKET`). No OneDrive files or folders are used for synchronization. **Do not change the Site to public:** the API intentionally relies on the private Sites access boundary.

The desktop's service credential is Windows-user encrypted in `sync-access.bin`, outside the source tree. It is only sent to the configured private Site via `OAI-Sites-Authorization`; automatic HTTP redirects are disabled. Browser requests use the same-origin authenticated session. There are no embedded cloud secrets in the mobile bundle.

Books retain their existing GUIDs. Each edition has an independent server revision. Writes use an atomic compare-and-set; removed editions retain a tombstone. An offline device compares its current books with its previous server snapshot. Independent changes merge. Conflicting edits stay local and are presented for review, with explicit choices for this device or the other version. A missing server revision is treated as a service reset, never as permission to erase a local catalog.

The desktop saves a catalog backup before its first sync. Catalog and baseline writes are ordered so interruption produces a recoverable retry or conflict rather than data loss. Both apps reject saves or removals from a stale open editor/detail view after a newer edition arrives. Sync runs on launch, after local changes, and every 45 seconds while open. Failed sync never rolls back a local save. Settings includes **Sync now** and the mobile link. Keep the desktop open until it reports synced after making changes you want on the phone.

## Development

```powershell
npm install
npm test
dotnet run --project tests\StarfleetLibrary.Sync.Tests\StarfleetLibrary.Sync.Tests.csproj
node scripts\build-mobile-host.mjs
npx wrangler d1 migrations apply starfleet-library --local --config host\wrangler.json
npm run dev
```

The local service binds only to `127.0.0.1:5173`. Its D1/R2 data is isolated under `host/.wrangler`, not the hosted database. The build places the actual Uno static assets in `dist/client` and the API Worker in `dist/server/index.js`; schema-only Drizzle migrations are included with the hosting metadata.

`tools/StarfleetLibrary.SyncSetup` can securely provision an existing service credential via stdin and seed/sync the catalog. Its `verify` command checks hosted metadata and cover checksums without modifying the catalog. `scripts/connect-private-sync.mjs` reads credential input without echoing it. Never put service credentials in commands, source files, browser code, screenshots or logs.

The template includes an optional native iOS head (`-p:EnableIos=true`). It is not the delivered mobile build: native iOS still requires a Mac/Xcode, Apple signing and a native scanner integration. The shipped scanner is in the Home Screen web app.

## Validation

The automated checks cover the existing core/storage behavior, per-edition sync, independent edits, explicit conflict resolution, duplicate formats, deletion propagation, lost acknowledgements and service resets. API tests cover compare-and-set revisions, input validation, cross-origin write rejection and cover checksums. Barcode tests exercise real EAN-13/ISBN and UPC-A image decoding plus invalid checksums.

Physical iPhone camera focus, lighting and Safari camera permissions require a device check. Desktop browser tests are not a substitute for that check.
