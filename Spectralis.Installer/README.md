# Spectralis.Installer

Packaging targets for the Avalonia app.

- `Windows/build-velopack.ps1` — Velopack build (`vpk pack`), the in-process update channel `VelopackUpdateService` checks against. This is the forward-looking release path.
- `Windows/build-squirrel.ps1` — Squirrel build (mirrors the legacy `build.ps1` / `spectralis.nuspec` pipeline, pointed at `Spectralis.App`). Migration-only — upgrades existing WinForms/old-Avalonia installs through the old feed. `// TODO 5.1.0: remove` once all users have moved to Velopack-packaged builds.
- `Mac/build-velopack.sh` — Velopack build (`vpk pack`) for macOS, the forward-looking release path. Produces `osx-arm64` and `osx-x64` channel feeds under `releases-velopack/`. Driven by the repo-root `build.sh` (the peer of `build.ps1`), which loads `.env` and can also trigger the `.dmg`.
- `Mac/build-dmg.sh` — standalone `.dmg` bundle (x64-only, runs under Rosetta on Apple Silicon) with code-signing / notarization hooks.
- `Linux/` — AppImage build (`build-appimage.sh`) and Velopack `linux-x64` feed (`build-velopack.sh`).

Every script refuses to run unless `SPECTRALIS_SPOTIFY_CLIENT_ID` and
`SPECTRALIS_DISCORD_CLIENT_ID` are set — releases must ship with both
client IDs baked in (see root `README.md`). The macOS `build.sh` wrapper
loads them from a repo-root `.env` if present; the other scripts expect
them exported in the calling shell.

Each directory contains a self-contained build script; CI invokes them per-platform.
