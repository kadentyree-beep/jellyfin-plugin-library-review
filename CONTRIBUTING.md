# Contributing

Keep scan paths read-only. Treat all filenames, metadata and remote error bodies as untrusted data; render as text. Never add a directory-delete path, bulk-delete shortcut, automatic repair, or automatic destructive retry.

Run the .NET and browser tests described in README. Linux CI exercises symlink cases; Windows covers file-lock and path conventions. Tests must use synthetic temporary files, never a user's actual media. Mock network calls; no production Radarr credentials in tests or logs.

Before a stable release, complete the disposable Jellyfin/Radarr integration checklist in docs/SAFETY.md. Include explicit compatibility information for every binary build. Keep release manifests and binaries versioned together. Alpha binaries are for testing and require opt-in replacement.

Suggested next contributions: standalone reference-provider interface, known edition runtimes, authenticated push notification integrations with duplicate suppression, pagination for large libraries, and per-item refresh. Do not claim audio/visual authenticity without evidence.
