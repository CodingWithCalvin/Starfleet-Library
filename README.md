# Starfleet Library

A personal Star Trek library with **native Uno Platform Windows and iPhone apps**, built with .NET 10. Track your collection and wishlist locally, and optionally sync your devices through an encrypted service in **your own Cloudflare account**.

The repository includes the desktop app, mobile app, shared library code, sync service, tests, and build workflows. You can fork it to manage your own collection without connecting to the maintainer's library or hosted service.

## Features

- Responsive cover gallery with corner ribbons identifying each edition’s format, wide cards and a scrollable table.
- Search and filters for series, reading status, format and books/comics, including COMIC and SNW labels.
- Owned books, wishlist, ISBN lookup and duplicate checks.
- Edition metadata, reading progress, ratings, finish dates, notes and insights.
- Local cover storage, offline image caching, JSON export and restore.
- Native iPhone Collection and Wishlist, Apple camera ISBN/UPC barcode scanning, and manual entry.
- End-to-end encrypted synchronization in your own Cloudflare account, with per-edition revision checks, offline changes and conflict review.

## Get the apps

Open the repository's **Actions** tab, choose a workflow, and download its artifact from a successful run. If you fork the repository, use the runs in your own fork.

| Workflow | Artifact | Use |
| --- | --- | --- |
| Windows desktop | `Starfleet-Library-Windows` | Extract the ZIP and run `StarfleetLibrary.Uno.exe`. |
| iPhone sideload IPA | `Starfleet-Library-iPhone-Sideload` | Extract the ZIP and install the IPA with Sideloadly. |
| Native iPhone | `native-iphone-build` | By default, a Mac simulator build for development; it cannot be installed on a phone. |

See the [iPhone sideloading instructions](docs/NATIVE-IOS.md#sideload-on-your-iphone-from-windows) for installation from Windows. GitHub's hosted Macs build the iPhone app, so you do not need to own a Mac. The native iPhone guide also covers signed builds for TestFlight.

## Run the Windows app from source

Install the **.NET 10 SDK** on a Windows PC, clone the repository, and double-click **Launch Uno.cmd** from the checkout. The launcher builds the Release app if its executable does not exist yet. The app can be used locally before setting up Cloudflare.

For development, open **StarfleetLibrary.Uno.slnx**. After changing source, close the app and rebuild from the repository root:

```powershell
dotnet build src/StarfleetLibrary.Uno/StarfleetLibrary.Uno.csproj -c Release -f net10.0-windows10.0.26100
```

The desktop project is in `src/StarfleetLibrary.Uno`; the native iPhone project is in `src/StarfleetLibrary.Mobile`. By default, catalogs, covers, and connection credentials are stored separately from the source checkout. See [storage and backup details](docs/UNO-WINDOWS.md).

## Fork and use your own Cloudflare account

Each deployment serves one private library shared between your devices. Forking copies the application and service code; you create your own hosted resources, catalog, and pairing credentials.

| Cloudflare resource | Purpose |
| --- | --- |
| Worker | Authenticates app requests and serves the sync API over HTTPS. |
| D1 database | Stores edition IDs, revisions, and encrypted catalog records. |
| R2 bucket | Stores encrypted cover images. |

The apps encrypt catalog content and covers before upload. The decryption key stays on your devices. Cloudflare can still see operational metadata such as record IDs, revisions, sizes, and request timing. Keep the R2 bucket private; the apps access it through the authenticated Worker.

### 1. Fork, clone, and install the tools

You will need a GitHub account, Git, a Windows PC with the .NET 10 SDK, **Node.js 22 or later** with npm, and a Cloudflare account with Workers, D1, and R2 available. Cloudflare usage is billed to your own account according to the services and plans you enable.

Fork [CodingWithCalvin/Starfleet-Library](https://github.com/CodingWithCalvin/Starfleet-Library), then clone your fork. Replace `YOUR-GITHUB-USERNAME` below with your GitHub user or organization name:

```powershell
git clone https://github.com/YOUR-GITHUB-USERNAME/Starfleet-Library.git
cd Starfleet-Library
npm ci
```

All remaining commands run from this repository root. In your fork's **Actions** tab, enable workflows if prompted. You can then run **Windows desktop** and **iPhone sideload IPA** manually; subsequent relevant pushes to `main` also trigger builds. See [GitHub's fork workflow guidance](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflows-in-forked-repositories).

### 2. Select your Cloudflare account

Sign in with Wrangler and identify the account you want to use:

```powershell
npx wrangler login
npx wrangler whoami
```

**The checked-in [Wrangler configuration](cloudflare/wrangler.jsonc) identifies the maintainer's deployment. Replace its `account_id` with your own account ID before creating resources or running any remote commands.** Choose a Worker `name` for your account as well. The examples below use `starfleet-library-private` for the Worker and database, and `starfleet-library-private-covers` for the bucket; you can use different names if you update both the commands and configuration consistently.

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
| `d1_databases[0].database_id` | The new database UUID printed by `wrangler d1 create`; replace the inherited UUID. |
| `r2_buckets[0].bucket_name` | The bucket name you just created. |

Keep the binding names **`DB`** and **`BUCKET`**, because the Worker uses those names. Keep `migrations_dir` set to `migrations`, `workers_dev` enabled, and `preview_urls` disabled. Do not enable public access on the R2 bucket.

After replacing the database ID, create the tables and deploy the Worker:

```powershell
npx wrangler d1 migrations apply DB --remote --config cloudflare/wrangler.jsonc
npm run deploy
```

Save the HTTPS Worker URL printed by deployment. The service rejects requests until its authentication secrets are configured in the next step. A browser request returning `401 Unauthorized` is expected; this endpoint is an app API, not a website.

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

Cloudflare's configured secrets hold the vault ID and the token's SHA-256 hash. Apps send the access token over HTTPS for authentication; the decryption key is never uploaded. Store the printed values as [Worker secrets](https://developers.cloudflare.com/workers/configuration/secrets/), not in source files or your public README.

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

Review `cloudflare/wrangler.jsonc` when merging upstream changes so your fork continues to use your own account and resources. Never commit pairing codes, encryption keys, access tokens, local catalogs, or Apple signing files. For TestFlight distribution, configure your own Apple signing credentials; if you change the iPhone application identifier, update the project and the workflow launch/validation settings together. See the [native iPhone guide](docs/NATIVE-IOS.md).

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

`npm run build` performs a dry-run Worker build; it does not deploy the service.

See [Windows setup and backups](docs/UNO-WINDOWS.md), [native iPhone builds and private sync](docs/NATIVE-IOS.md), and [validation notes](docs/UNO-VALIDATION.md).
