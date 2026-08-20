# Spectralis Docs

This directory is the canonical documentation home for Spectralis.

---

## Architecture & Philosophy

| Document | What it covers |
|---|---|
| [architecture.md](architecture.md) | The Avalonia system — projects, seams, modules, security posture, status & roadmap |
| [legacy-winforms.md](legacy-winforms.md) | The legacy WinForms app — building it, data compatibility, behavior differences |
| [guidelines.md](guidelines.md) | Product north star — pillars, design direction, non-goals |
| [standards.md](standards.md) | Extension rules — visualizers, themes, settings, per-track stores, controls, OBS overlay |
| [creator-tools.md](creator-tools.md) | Built-in creator workflows — Lyrics Timing Studio, Content Warnings |
| [cdn-contract.md](cdn-contract.md) | CDN endpoint shapes the app expects from `cdn.deltavdevs.com` |
| [api-contract.md](api-contract.md) | Service routing contract — CDN vs API split, all routes |
| [song-wars-implementation-plan.md](song-wars-implementation-plan.md) | Song Wars tournament mode implementation plan |

## Capsule & Data Formats

| Document | What it covers |
|---|---|
| [formats/spectralis-capsule.md](formats/spectralis-capsule.md) | `.spectralis` single-track signed capsule format |
| [formats/spectral-album-world.md](formats/spectral-album-world.md) | `.spectral` album world capsule format (multi-track + interactive HTML) |
| [formats/reactive-timeline.md](formats/reactive-timeline.md) | `.spectralis-reactive.json` sidecar format |
| [formats/metadata-embedding.md](formats/metadata-embedding.md) | ID3v2 embedded modules — WASM, HTML, Markdown, video |

## Components

| Document | What it covers |
|---|---|
| [../backend/README.md](../backend/README.md) | Rust Shared Play backend (Railway) |
| [../extension/README.md](../extension/README.md) | Chromium browser extension |
| [../metadata/STANDARDS.md](../metadata/STANDARDS.md) | Metadata packing standard and tool spec |
| [../metadata/typography/README.md](../metadata/typography/README.md) | Kinetic typography generator tool |

## Legal

| Document | What it covers |
|---|---|
| [legal/terms-of-service.md](legal/terms-of-service.md) | Terms for official builds, hosted services, Shared Play, integrations, and creator content |
| [legal/privacy-policy.md](legal/privacy-policy.md) | Privacy disclosures for the desktop app, Shared Play, hosted services, and third-party integrations |

## Development

| Document | What it covers |
|---|---|
| [development/worklog.md](development/worklog.md) | Active sprint log |
| [../README.md](../README.md) | Project overview and setup |

---

## Quick Reference — Capability Constants

| Constant | File format | Effect |
|---|---|---|
| `webview.localContent` | `.spectralis`, `.spectral` | Capsule can load local HTML in WebView2 |
| `album.world` | `.spectral` only | Capsule may include an interactive album world |
| `visualizer.wasm` | `.spectralis` | Capsule can embed a WASM visualizer |
| `visualizer.multiLayer` | `.spectralis` | Capsule can compose multiple visualizer layers |
| `visualizer.shaderPack` | `.spectralis` | Capsule can supply shader packs |
| `sharedPlay.hostCapsule` | `.spectralis` | Capsule can be hosted via Shared Play |
| `sharedPlay.packageUpload` | `.spectralis` | Capsule assets may be uploaded for Shared Play |
| `timeline.appControl` | `.spectralis` | Capsule's timeline may issue app control events |
