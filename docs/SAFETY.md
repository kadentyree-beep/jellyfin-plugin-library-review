# Replacement safety and recovery

## Trust boundary

The server administrator, configured Radarr instance, and host filesystem are trusted. This is not protection against a malicious local administrator or another process deliberately racing filesystem operations. File hashes, path bounds, no-link checks, single-use confirmations, and atomic staging protect against ordinary stale reviews, replaced files, incorrect path mapping, duplicate clicks, and concurrent imports. Backups remain necessary.

The scheduled task cannot invoke replacement. There is no bulk-delete API and no directory-delete call in production code. The only deletion is `File.Delete(staged)` after verification. Tests use disposable temporary directories.

## Failure stages

| Stage | Outcome |
|---|---|
| Preview/identity/hash failure | Nothing changed; no blocklist operation |
| File changed before staging | Action rejected |
| Captured file fails verification | Restore original name if free; do not overwrite a new import |
| Blocklist request fails or times out | Attempt restoration; blocklist may have succeeded remotely; inspect before retry |
| Crash after staging | An adjacent `.library-review-<random>.quarantine` file may remain; do not delete it blindly |
| Delete succeeds, refresh/search fails | File remains deleted; activity records deletion. Fix Radarr and search from Radarr; do not repeat deletion |
| New file appears at original path after staging | Left untouched; only the uniquely staged original is eligible for removal |

The UI has a 30-minute request timeout for large-file hashes. Closing a page can cancel a request; the action log is the source of progress, not the button state. Jellyfin restart invalidates outstanding previews.

## Recovering a staged file

Check the plugin's activity log and the exact adjacent quarantine file. Confirm its size/hash against the preview. If the original path is empty, rename that exact quarantine file back to the original filename. If a new file already occupies the name, retain both and investigate. Do not overwrite the new file or recursively delete the movie directory.

Radarr's blocklist operation can trigger its own automatic failed-download search; this behavior is outside the plugin transaction. The plugin checks for active downloads before starting and again before its explicit search, but there is no cross-process transaction lock with Radarr. It never edits unrelated queues.

## Required manual integration checks before production enablement

1. Test plugin loading and admin/non-admin authorization on the exact Jellyfin version.
2. Use a disposable Radarr movie with an imported fixture and traceable grab/import history.
3. Verify preview is read-only; wrong confirmation, stale token and changed mapping are rejected.
4. Put sidecars and a second video beside the fixture. Confirm only the selected file is removed.
5. Interrupt Radarr availability before blocklisting and verify the captured file is restored.
6. Verify Radarr sees the missing file and starts a new search; confirm the resulting replacement independently.
7. Test playback-in-progress behavior for your OS. Windows may refuse to stage an open file; Linux may let ongoing playback finish from the open file handle.

Do not test deletion on a valuable production movie just to demonstrate that the button works.
