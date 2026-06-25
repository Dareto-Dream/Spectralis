# Spectralis.Installer

Packaging targets for the Avalonia app.

- `Windows/build-velopack.ps1` — Velopack build (`vpk pack`), the in-process update channel `VelopackUpdateService` checks against. This is the forward-looking release path.
- `Windows/build-squirrel.ps1` — Squirrel build (mirrors the legacy `build.ps1` / `spectralis.nuspec` pipeline, pointed at `Spectralis.App`). Migration-only — upgrades existing WinForms/old-Avalonia installs through the old feed. `// TODO 5.1.0: remove` once all users have moved to Velopack-packaged builds.
- `Mac/` — `.dmg` bundle with code-signing hooks.
- `Linux/` — AppImage build.

Every script refuses to run unless `SPECTRALIS_SPOTIFY_CLIENT_ID` and
`SPECTRALIS_DISCORD_CLIENT_ID` are set — releases must ship with both
client IDs baked in (see root `README.md`).

Each directory contains a self-contained build script; CI invokes them per-platform.
