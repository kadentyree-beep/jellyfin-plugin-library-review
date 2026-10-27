# Jellyfin Library Review

Find movies worth reviewing—not a promise that every file is authentic.

Library Review is an **administrator-only Jellyfin plugin** with a review page for possible runtime, edition, audio-language, and commentary mismatches. Scans never delete, replace, re-encode, or upload media.

**Status: experimental alpha.** Built against Jellyfin **12.2.0 / .NET 10**. Older Jellyfin versions are not supported by this binary. Automated tests and browser fixtures are included; production Jellyfin installation and destructive replacement with a real Radarr instance still require a disposable-library integration test. Leave replacement disabled on a valuable library until you have tested your setup and backups.

## What it checks

- Runtime difference exceeding both ten minutes and ten percent of the reference runtime.
- Missing preferred-language tags (reported as uncertainty, not proof of foreign audio).
- Default audio labeled commentary, or only commentary tracks in the preferred language.
- Filenames suggesting featurettes, Extraction Mode, behind-the-scenes content, or RiffTrax.

The plugin uses Jellyfin's existing media information, avoiding full video decoding on every scan. It scans every 15 minutes through Jellyfin Scheduled Tasks. You can change that schedule or run **Check library now**. Newly imported files are checked on the next scan after Jellyfin has indexed them.

**Independent runtime reference currently comes from optional Radarr metadata.** Without Radarr, the plugin still checks audio and filenames and explicitly reports that runtime comparison is unavailable. It never compares Jellyfin's file-derived runtime to itself as evidence of correctness. Direct TMDB integration and known-edition lookup are future work.

## Review page

Open **Dashboard → Plugins → Library Review → Settings** (also exposed as a plugin menu entry in supported Jellyfin Web versions).

Each movie shows the exact file, findings, measured/reference runtime, and review state:

- **Accept this edition**: acknowledge a legitimate alternate cut.
- **Dismiss**: acknowledge a warning you have investigated.
- **Mark as a problem**: keep it in the unresolved list; does not change media.
- **Reopen**: return it to review.

Decisions are tied to the file path, size, modification time, and findings. Replacing a file or changing the findings reopens review. Metadata-only checks cannot detect an adversarial same-size/same-timestamp replacement; the destructive action separately uses full SHA-256 verification.

The page is for administrators. A server plugin cannot insert a movie-page badge into every phone/TV app. **Email, push alerts, client badges, and automatic fixes are not included in v0.1.** New findings appear on this review page, not as unsolicited emails or universal popups.

## Optional: block release and find a replacement

Disabled by default. Requires Radarr, a single explicit movie-root mapping, and Jellyfin filesystem write permission. Normal scans need read-only access.

1. Configure Radarr URL/key and the same movie root as seen by both apps, for example `/media/movies` in Jellyfin and `/data/movies` in Radarr.
2. Explicitly enable administrator-confirmed replacement.
3. Inspect the warning and click **Mark as a problem**.
4. Click **Block release and find replacement…**. A preview verifies the entire file and displays its exact path, size, SHA-256 and original release.
5. Type the movie title exactly and confirm the destructive action within ten minutes.

The operation:

- Matches the TMDB identity, exact mapped path, size, Radarr movie-file ID, import history, download ID, and original grab. Missing or ambiguous history disables the action.
- Rejects symlinks/junctions, paths outside the configured root, unsupported file types, changed files, and active downloads for that movie.
- Rechecks the full hash and moves **only that file** to a unique adjacent `.quarantine` name. It verifies the captured file before proceeding.
- Marks that original Radarr grab failed to blocklist the **release**, not the movie title.
- Deletes only the captured video file, never its folder, subtitles, artwork, or other editions. It deliberately does not use Radarr's file/bulk-delete endpoints, which can handle companion files.
- Requests a Radarr refresh and replacement search using the movie's existing quality profile. Radarr's failed-download handling can also start a search when blocklisting occurs.

Deletion is permanent; replacement availability and correctness are not guaranteed. Full-file hashing can take minutes and causes disk reads. This action does **not** bypass Radarr quality/language settings.

Tokens are single-use and expire. Changing settings invalidates the preview. Do not retry blindly after a timeout: inspect **Replacement activity**, Radarr, and the filesystem. See [recovery and safety](docs/SAFETY.md).

## Install / build

Use the alpha release ZIP only with the stated Jellyfin version. Extract `Jellyfin.Plugin.LibraryReview.dll` into a dedicated `Library Review_0.1.0.0` folder inside your Jellyfin plugin directory, then restart Jellyfin at a convenient time. Keep the existing standalone audit if you have one until the plugin is verified. A repository catalogue manifest is supplied with releases.

Build from source with the .NET 10 SDK:

```sh
dotnet build src/LibraryReview -c Release
dotnet test tests/LibraryReview.Tests -c Release
npm ci
npx playwright install chromium
npm test
```

Copy only the plugin DLL from `src/LibraryReview/bin/Release/net10.0/`. Jellyfin supplies its own server assemblies; do not copy the SDK or Jellyfin package DLLs into the plugin folder.

## Privacy and security

- No media uploads, telemetry, external AI calls, or third-party content-identification service.
- Optional requests go only to the configured Radarr endpoint. HTTPS certificate validation is not disabled, and redirects are refused to avoid forwarding the API key.
- Radarr keys are stored in the Jellyfin plugin configuration, like other connection plugins. Protect the config directory and backups; use HTTPS on untrusted networks.
- Admin-only endpoints; explicit authentication headers on writes. Never expose an unauthenticated public plugin endpoint.
- State and action history remain in the Jellyfin plugin-config directory under `library-review` and may contain private movie names and paths. Do not attach these unredacted to public issues.

## Limitations

Movies only; one mapped root and Radarr instance in this first version. No TV episode analysis, visual identification, speech recognition, full corruption scan, forced replacement, bulk deletion, or unattended repair. Runtime variation is often legitimate. Audio labels may be wrong. A clean result is **not** proof of authenticity.

MIT licensed. Contributions should include tests, especially for anything that can write to the filesystem. Please use disposable fixtures rather than real libraries when testing destructive behavior.
