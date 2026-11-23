# Starfleet Library

A personal Star Trek library with **native Uno Platform Windows and iPhone apps**, built with .NET 10, and a **read-only browser reader**. Track your collection and wishlist locally, and optionally sync your devices through an encrypted service in **your own Cloudflare account**.

The repository includes the desktop app, mobile app, shared library code, sync service, tests, and build workflows. You can fork it to manage your own collection without connecting to the maintainer's library or hosted service.

## Features

- Responsive cover gallery with corner ribbons identifying each edition’s format, wide cards and a scrollable table.
- Cover tiles combine copies with the same title and format, ignoring capitalization and extra spaces, and show a copy count. Details let you choose each copy; card, table and list layouts retain separate records. Counts follow the current filters and keep Collection and Wishlist separate.
- Search and filters for series, reading status, format and books/comics, including COMIC and SNW labels.
- Owned books, wishlist, ISBN lookup and duplicate checks.
- ISBN lookup is at the top of Add/Edit and fills the main series book number when a verified ISBN or explicit series-metadata match is available. Unknown or ambiguous numbers stay blank.
- **Missing books** shows gaps in numbered series by format on Windows, iPhone and the browser. Duplicate copies count once; wishlist entries remain marked as missing. Windows **Review numbering** offers quick edits and known ISBN matches. [Numbering guide and reference sources](docs/NUMBERED-BOOKS.md).
- **Buying guide** combines wishlist entries, numbered gaps and story-arc gaps into a compact list of titles, numbers/parts, formats and ISBN references. It merges overlapping suggestions, recognizes known ISBNs even when a saved number is blank, and offers a copyable shopping checklist.
- Cover artwork can be overridden with a direct image URL in the edition editor. Publisher pages, including Simon & Schuster's **Resources and Downloads → Book Cover Image**, are useful when imported ebooks contain an interior title page instead of a cover. A failed custom URL falls back to imported artwork, then the Open Library ISBN cover; working cached images remain available offline.
- Switch Missing books to **Story arcs** to find missing parts by title. The initial seven sets include Double Helix, The Q Continuum, The Genesis Wave, Cold Equations, A Time to..., Millennium and Destiny. Both views filter by Series and Format, defaulting to **Print books** (paperback + hardcover). An ebook omnibus does not fill a print gap.
- Edition metadata, reading progress, ratings, finish dates, notes and insights.
- Local cover storage, offline image caching, JSON export and restore.
- Native iPhone Collection and Wishlist, Apple camera ISBN/UPC barcode scanning, and manual entry.
- Read-only browser access to Collection and Wishlist, with cover, card and table layouts, search, comic/series/format filters, and edition details. Choose public access or private password sign-in for your deployment.
- End-to-end encrypted synchronization in your own Cloudflare account, with per-edition revision checks, offline changes and conflict review.

## Get the apps

Open the repository's **Actions** tab, choose a workflow, and download its artifact from a successful run. If you fork the repository, use the runs in your own fork.

| Workflow | Artifact | Use |
| --- | --- | --- |
| Windows desktop | `Starfleet-Library-Windows` | Extract the ZIP and run `StarfleetLibrary.Uno.exe`. |
| iPhone sideload IPA | `Starfleet-Library-iPhone-Sideload` | Extract the ZIP and install the IPA with Sideloadly. |
| Native iPhone | `native-iphone-build` | By default, a Mac simulator build for development; it cannot be installed on a phone. |

See the [iPhone sideloading instructions](docs/NATIVE-IOS.md#sideload-on-your-iphone-from-windows) for installation from Windows. GitHub's hosted Macs build the iPhone app, so you do not need to own a Mac. The native iPhone guide also covers signed builds for TestFlight.

## Open your library in a browser

After configuring Cloudflare below, open **your own Worker HTTPS address** in a desktop or phone browser. The root address serves the reader; there is no separate Pages deployment or ChatGPT-hosted component.

### Public access without a login

To make your collection and wishlist viewable by anyone, set `vars.PUBLIC_READER_ENABLED` to the string `"true"` in your ignored `cloudflare/wrangler.jsonc`, apply migrations, and deploy:

```powershell
npx wrangler d1 migrations apply DB --remote --config cloudflare/wrangler.jsonc
npm run deploy
dotnet run --project tools/StarfleetLibrary.SyncSetup/StarfleetLibrary.SyncSetup.csproj -c Release -- publish-public
```

Run the publication command on the Windows profile already connected to your library, after completing the native sync setup below. It publishes the synced catalog without changing private catalog records. The Worker address then opens directly to Collection with no password, connection code, or browser setup. The Windows app can be off while you browse.

Public access includes titles, authors, series, ISBNs, edition and publisher details, format, book/comic labels, ownership/wishlist status, page counts, and covers. Notes, ratings, reading progress, dates, and credentials are excluded. A separate public catalog and cover copies are stored in Cloudflare; the original encrypted catalog, encrypted covers, and device credentials remain intact. Visitors cannot add, edit, delete, or sync books.

Updated native apps maintain the public copy during normal sync. After enabling this on an existing deployment, update your apps or run `publish-public` again after syncing changes with an older app. Publication is checked against the current encrypted edition revision, so a changed edition is hidden until its matching public copy is published. Set `PUBLIC_READER_ENABLED` back to `"false"` and redeploy to disable public access; stored public copies remain in your account.

The checked-in example defaults to private access. Public mode never uploads the library encryption key to the Worker.

### Private password sign-in

With `PUBLIC_READER_ENABLED` absent or set to `"false"`, use the following optional password setup instead:

1. **Once, to enable password sign-in:** copy **Settings → Show iPhone connection code** from the Windows app, or use your existing recovery copy. Paste it into **Private connection code** at your Worker address.
2. Choose a unique **library password** of at least 14 characters and select **Save password and open library**. Save the password and recovery connection code in your password manager.
3. **After setup, on any browser or phone:** open your Worker address and enter only your library password. The Windows app does not need to be running or accessible, and a new browser does not need a connection code. The site reads the latest catalog already synced to Cloudflare.

The reader can browse Collection and Wishlist, search titles/authors/ISBNs, exclude comics, switch between Covers/Cards/Table, and open edition details. The visible Back button and browser Back preserve the search, filters, layout, and list position. On phones, expand **Filters** when needed. Use **Refresh** to see newly synced changes; adding or editing books and camera scanning remain in the native apps.

During setup, Web Crypto uses PBKDF2-SHA-256 (600,000 iterations) and HKDF with separate purposes to derive independent authentication and key-wrapping keys. The browser encrypts the library key with AES-256-GCM and uploads only that encrypted wrapper, a random salt, and a hash of the derived authentication proof. The wrapper is bound to the service origin, vault, and password-configuration ID. **The password, plaintext library key, and wrapping key never go to the Worker.** The native write token is used only to authorize initial setup or password reset; it is not included in the encrypted wrapper. Cloudflare stores the encrypted catalog, encrypted covers, and password-encrypted key, plus authentication hashes and operational metadata. A strong, unique password protects the stored wrapper against offline guessing if the database is exposed.

Each password sign-in obtains a separate **read-only session token** and decrypts the library key locally. No password or new password-session credentials are saved in local storage. Sessions expire after 30 days; expiry just requires entering your library password again, without the Windows app. **Sign out** clears the displayed catalog and cover URLs and revokes the session when online. Login attempts are rate limited using the `LOGIN_RATE_LIMITER` binding (10 attempts per minute per source IP, approximately, within each Cloudflare location). See [Cloudflare's rate limiting behavior](https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/).

Use **Set up or reset library password** and your recovery connection code to change a forgotten password. Password reset invalidates older password sessions on their next authenticated request; it does not change the catalog encryption key or native app credentials. Removing the parent native device token hash also revokes password access and its sessions. Existing connections saved using the original browser-only password remain available under **Use a previously saved browser connection**; that old password is local to one browser until you complete the new library-password setup.

This reader requires an internet connection to load the catalog and covers. It does not replace the native apps' offline storage. Browser privacy depends on trusting the hosted reader code and the device running it; use a trusted browser and sign out when finished.

## Run the Windows app from source

Install the **.NET 10 SDK** on a Windows PC, clone the repository, and double-click **Launch Uno.cmd** from the checkout. The launcher builds the Release app if its executable does not exist yet. The app can be used locally before setting up Cloudflare.

For development, open **StarfleetLibrary.Uno.slnx**. After changing source, close the app and rebuild from the repository root:

```powershell
dotnet build src/StarfleetLibrary.Uno/StarfleetLibrary.Uno.csproj -c Release -f net10.0-windows10.0.26100
```

The desktop project is in `src/StarfleetLibrary.Uno`; the native iPhone project is in `src/StarfleetLibrary.Mobile`. By default, catalogs, covers, and connection credentials are stored separately from the source checkout. See [storage and backup details](docs/UNO-WINDOWS.md).

## Fork and use your own Cloudflare account

Each deployment serves one library shared between your devices, with private synchronization and optional public browsing. Forking copies the application and service code; you create your own hosted resources, catalog, and pairing credentials.

| Cloudflare resource | Purpose |
| --- | --- |
| Worker | Serves the browser reader, authenticates private API requests, and optionally serves the public catalog over HTTPS. |
| D1 database | Stores encrypted catalog records, revisions, optional password authentication data, and the limited public catalog when enabled. |
| R2 bucket | Stores encrypted cover images and separate public cover copies when enabled. |

The apps encrypt the full catalog and covers before upload. The plaintext decryption key stays on your devices; enabling browser password sign-in stores an additional password-encrypted copy. Public mode also uploads the limited book fields and cover copies described above in plaintext. Cloudflare can see operational metadata such as record IDs, revisions, sizes, and request timing. Keep the R2 bucket private; all cover access goes through the Worker.

### 1. Fork, clone, and install the tools

You will need a GitHub account, Git, a Windows PC with the .NET 10 SDK, **Node.js 22 or later** with npm, and a Cloudflare account with Workers, D1, and R2 available. Cloudflare usage is billed to your own account according to the services and plans you enable.

Fork [CodingWithCalvin/Starfleet-Library](https://github.com/CodingWithCalvin/Starfleet-Library), then clone your fork. Replace `YOUR-GITHUB-USERNAME` below with your GitHub user or organization name:

```powershell
git clone https://github.com/YOUR-GITHUB-USERNAME/Starfleet-Library.git
cd Starfleet-Library
npm ci
```

All remaining commands run from this repository root. In your fork's **Actions** tab, enable workflows if prompted. You can then run **Windows desktop** and **iPhone sideload IPA** manually; subsequent relevant pushes to `main` also trigger builds. See [GitHub's fork workflow guidance](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflows-in-forked-repositories).

### 2. Create your local configuration and select your account

On a fresh checkout, copy the [example configuration](cloudflare/wrangler.example.jsonc):

```powershell
Copy-Item cloudflare/wrangler.example.jsonc cloudflare/wrangler.jsonc
```

`cloudflare/wrangler.jsonc` is ignored by Git and holds your deployment settings. If you already have that file, edit it rather than overwriting it. The checked-in example contains only placeholder IDs; `npm run build` and CI use that example for an offline build. Local development and deployment use your local `wrangler.jsonc`.

Sign in with Wrangler and identify the account you want to use:

```powershell
npx wrangler login
npx wrangler whoami
```

**In your local `cloudflare/wrangler.jsonc`, replace the all-zero `account_id` with your own account ID before creating resources or running any remote commands.** Choose a Worker `name` for your account as well. The examples below use `starfleet-library-private` for the Worker and database, and `starfleet-library-private-covers` for the bucket; you can use different names if you update both the commands and configuration consistently.

### 3. Create your database and cover bucket

With your own `account_id` in the configuration, create the resources:

```powershell
npx wrangler d1 create starfleet-library-private --config cloudflare/wrangler.jsonc --update-config=false
npx wrangler r2 bucket create starfleet-library-private-covers --config cloudflare/wrangler.jsonc --update-config=false
```

Edit `cloudflare/wrangler.jsonc` using the values from your account:

| Setting | Value in your fork |
| --- | --- |
| `account_id` | Your Cloudflare account ID. |
| `name` | Your chosen Worker name. |
| `d1_databases[0].database_name` | The database name you just created. |
| `d1_databases[0].database_id` | The new database UUID printed by `wrangler d1 create`; replace the all-zero placeholder. |
| `r2_buckets[0].bucket_name` | The bucket name you just created. |

Keep the binding names **`DB`**, **`BUCKET`**, and **`ASSETS`**, because the Worker uses those names. Keep the example's `assets` block, including `run_worker_first: true`, so security headers and API routing run before static asset serving. Keep `migrations_dir` set to `migrations`, `workers_dev` enabled, and `preview_urls` disabled. Do not enable public access on the R2 bucket.

Keep the example's `ratelimits` block and **`LOGIN_RATE_LIMITER`** binding as well. Its `namespace_id` is a developer-chosen positive integer, not an account credential; choose a different value if another application in your account already uses that namespace. Password login fails closed if the binding is absent.

After replacing the database ID, create the tables and deploy the Worker:

```powershell
npx wrangler d1 migrations apply DB --remote --config cloudflare/wrangler.jsonc
npm run deploy
```

Save the HTTPS Worker URL printed by deployment. Its root address displays the reader's connection screen. Catalog and cover APIs still return `401 Unauthorized` without valid credentials, and pairing fails closed until the authentication secrets are configured in the next step. No library data is included in the public static files.

Cloudflare references: [D1 creation and migrations](https://developers.cloudflare.com/d1/wrangler-commands/) and [R2 bucket creation](https://developers.cloudflare.com/r2/buckets/create-buckets/).

### 4. Create your private library credentials

Close the desktop app while running the setup tool. Replace the example URL below with the exact HTTPS URL from your deployment, including the trailing slash:

```powershell
dotnet run --project tools/StarfleetLibrary.SyncSetup/StarfleetLibrary.SyncSetup.csproj -c Release -- initialize https://YOUR-WORKER.YOUR-SUBDOMAIN.workers.dev/
```

This creates a vault ID, a random encryption key, and an access token on your PC. It saves the connection protected for your Windows user and prints JSON containing only **`VAULT_ID`** and **`DEVICE_TOKEN_HASHES`**. It does not print the raw access token or encryption key. If this Windows profile is already paired to a different vault, the tool stops instead of overwriting that connection.

Set the two Worker secrets. When each command prompts, paste only the matching value from that JSON, without the field name or quotation marks:

```powershell
npx wrangler secret put VAULT_ID --config cloudflare/wrangler.jsonc
npx wrangler secret put DEVICE_TOKEN_HASHES --config cloudflare/wrangler.jsonc
```

Cloudflare's configured secrets hold the vault ID and the token's SHA-256 hash. Apps send the access token over HTTPS for authentication; the plaintext decryption key is never uploaded. Store the printed values as [Worker secrets](https://developers.cloudflare.com/workers/configuration/secrets/), not in source files or your public README.

### 5. Sync your catalog and pair the iPhone

With the desktop app still closed, sync the local catalog and verify that the service can return and decrypt the records and covers:

```powershell
dotnet run --project tools/StarfleetLibrary.SyncSetup/StarfleetLibrary.SyncSetup.csproj -c Release -- sync
dotnet run --project tools/StarfleetLibrary.SyncSetup/StarfleetLibrary.SyncSetup.csproj -c Release -- verify
```

An empty catalog is valid; you can add books afterward. Existing local books and covers are uploaded, and the sync tool backs up the catalog before its first cloud sync.

Reopen the desktop app, then:

1. Open **Settings** and select **Show iPhone connection code**.
2. Install your iPhone build using the [sideloading guide](docs/NATIVE-IOS.md#sideload-on-your-iphone-from-windows).
3. Open the connection control in the iPhone app, paste the code, and connect.

The connection code contains the service URL and credentials needed to access and decrypt your library. Keep it private and save a recovery copy in a password manager. Each device retains its local catalog for offline use and syncs while the app is open. Keep local backups too: hosted ciphertext cannot recover a lost decryption key.

### Maintaining your fork

The included GitHub Actions workflows build the apps and test the sync service; **they do not deploy to Cloudflare**. Deploy service changes from your configured checkout with `npm run deploy`, applying new D1 migrations first when needed. Your basic setup does not require Cloudflare credentials in GitHub.

To upgrade an older deployment, copy the `assets`, `ratelimits`, and `vars` blocks from the example into your ignored local config, apply all D1 migrations (through `0004_public_catalog.sql`), then deploy. These add browser authentication and public catalog storage, leaving existing encrypted records, covers, native credentials, and encryption keys intact. Choose public access or complete the private password setup described above. See [Cloudflare's static asset binding documentation](https://developers.cloudflare.com/workers/static-assets/binding/) for the asset configuration.

When merging upstream changes, compare `cloudflare/wrangler.example.jsonc` with your ignored local `cloudflare/wrangler.jsonc` and apply any new settings while preserving your account and resource IDs. Do not force-add the local config to Git. Never commit pairing codes, encryption keys, access tokens, local catalogs, or Apple signing files. For TestFlight distribution, configure your own Apple signing credentials; if you change the iPhone application identifier, update the project and the workflow launch/validation settings together. See the [native iPhone guide](docs/NATIVE-IOS.md).

## Checks

Run these from the repository root after installing dependencies with `npm ci`:

```powershell
dotnet run --project tests/StarfleetLibrary.Core.Tests/StarfleetLibrary.Core.Tests.csproj
dotnet run --project tests/StarfleetLibrary.Uno.Tests/StarfleetLibrary.Uno.Tests.csproj
dotnet run --project tests/StarfleetLibrary.Sync.Tests/StarfleetLibrary.Sync.Tests.csproj
dotnet run --project tests/StarfleetLibrary.PrivateSync.Tests/StarfleetLibrary.PrivateSync.Tests.csproj
npm test
npm run build
```

`npm run build` performs a dry-run Worker build using the checked-in example config; it needs no personal Cloudflare configuration and does not deploy the service. `npm run dev` and `npm run deploy` use your ignored local config.

The browser tests include a synthetic fixture generated by the real .NET `VaultCrypto` implementation. To regenerate it after a deliberate protocol change, run `dotnet run --project tests/StarfleetLibrary.PrivateSync.Tests -- --browser-fixture tests/fixtures/browser-vault.json`, then `npm test`. Its all-zero credentials are test data and cannot access a real deployment.

See [Windows setup and backups](docs/UNO-WINDOWS.md), [native iPhone builds and private sync](docs/NATIVE-IOS.md), and [validation notes](docs/UNO-VALIDATION.md).
