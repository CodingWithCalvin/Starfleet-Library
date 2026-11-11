# Native iPhone app and private sync

The mobile project targets `net10.0-ios` with Uno's native UIKit renderer. It contains Collection and Wishlist, local file persistence, format ribbons, filters, edition editing and native AVFoundation camera barcode scanning. ISBNs check existing editions before lookup or adding another copy. UPC/EAN codes without an ISBN can be read but require a title/ISBN search to identify the book. Camera frames never leave the device. Camera permission denial, cancellation and app backgrounding stop camera use; manual ISBN entry remains available.

## GitHub-hosted Mac builds

`.github/workflows/ios.yml` builds on GitHub's `macos-26` runners, with Xcode 26.6 and the .NET 10.0.302.1 iOS workload set pinned together. Pushes to main build an unsigned Release/AOT simulator application, with LLVM optimization disabled only for simulator builds to shorten CI compilation. The workflow allows time for the simulator's first boot, installs and launches the app, checks that its process stays running, and captures a screenshot. The application ZIP is uploaded before the launch check so it remains available if simulator startup fails; the screenshot is a separate artifact. The application artifact is for a Mac simulator, not installation on an iPhone. Signed device builds retain the default Release optimization.

An Apple Developer membership is required for TestFlight distribution. Once enrolled, register `com.calvin.starfleetlibrary.mobile`, create the distribution certificate/profile and add these GitHub Actions secrets: `IOS_CERTIFICATE_P12_BASE64`, `IOS_CERTIFICATE_PASSWORD`, `IOS_PROVISION_PROFILE_BASE64`, `IOS_KEYCHAIN_PASSWORD`, `IOS_SIGNING_IDENTITY`, and `IOS_PROFILE_NAME`. Run the workflow with `signed=true` to produce an IPA for TestFlight upload. No signing material belongs in source control. The workflow currently produces the signed artifact; automatic App Store Connect upload can be configured after enrollment.

On this Windows PC, `dotnet build src/StarfleetLibrary.Mobile/StarfleetLibrary.Mobile.csproj -f net10.0-ios -p:EnableCodeSigning=false` checks native C#/XAML compilation. The Mac workflow performs the actual Apple application build. `-p:MobileWindowsPreview=true` builds the same mobile interface as a Windows app for layout testing; it does not test the iPhone camera.

## Cloudflare service

The standalone Worker in `cloudflare/worker.js` authenticates every request itself. D1 stores edition IDs, revisions and encrypted payloads. R2 stores encrypted covers under keyed identifiers. There is no web app, public bucket, browser login, ChatGPT identity or Sites dispatch dependency.

The app encrypts metadata, notes, tombstones and cover references with AES-256-GCM before upload. Covers are encrypted separately. Fresh random nonces are used for every encryption; authenticated context binds entries to the vault, edition ID and revision, and covers to their keyed identifier. The encryption key is never sent to Cloudflare. Cloudflare can still observe request timing, opaque object IDs, revision numbers and encrypted sizes. Device tokens authenticate HTTPS requests; the Worker stores only SHA-256 token hashes. Removing a hash from its secret revokes that token. A stolen pairing code grants both access and decryption; keep a recovery copy in a password manager.

Windows protects its configuration with the current user's Windows data protection. iPhone stores it in non-synchronizing, device-only Keychain storage. Local catalogs and covers live in each application's private data directory and remain usable offline. Lookup of a new ISBN contacts Open Library only when explicitly requested.

Provision resources in your own account with Wrangler:

```powershell
npx wrangler login
npx wrangler d1 create starfleet-library-private
npx wrangler r2 bucket create starfleet-library-private-covers
# Put the returned D1 ID in cloudflare/wrangler.jsonc.
npx wrangler d1 migrations apply DB --remote --config cloudflare/wrangler.jsonc
```

Initialize the desktop connection using `tools/StarfleetLibrary.SyncSetup initialize https://YOUR-WORKER.workers.dev/`. It generates the encryption key and device token locally, saves them encrypted for your Windows user, and prints only `VAULT_ID` and `DEVICE_TOKEN_HASHES`. Configure those two Worker secrets with `wrangler secret put --config cloudflare/wrangler.jsonc`, then deploy with `npm run deploy`. The service fails closed until configured. Do not enable public R2 access. The Worker never needs the encryption key or the original token.

Run `SyncSetup sync` to back up and seed the catalog, and `SyncSetup verify` to verify encrypted round trips and every cover checksum. The desktop uses a separate `private-sync-baseline.json`; the old service's revision history is not reused. In desktop Settings, **Show iPhone connection code** reveals the pairing code. On iPhone, tap **Pair your private library**, paste the code and connect. Both devices then sync while open, retaining local edits during outages and requiring review for conflicting edits.

## Previous hosted copy

The prior Sites service is retired. After verifying the encrypted Cloudflare migration, its 228 live catalog records and 228 cover objects were deleted; both storage counts are now zero and the old application returns HTTP 410. Its desktop credential is disabled and the source deployment integration removed. The Sites connector has no project-delete action, so an empty Site entry and provider-managed version/backup retention may remain until removed through the hosting interface. The Windows catalog and cover files are intact.
