# Spectral MCP

A local MCP server that lets agentic tools (Claude Code, or any other MCP client) scaffold,
validate, sign, and embed Spectralis content directly, instead of hand-walking the format
rules and packing conventions from scratch every session.

It covers all three creator-facing formats:

- **`.spectralis` capsules** — single-track, optionally with an embedded HTML/WASM visualizer,
  reactive timeline, story page. `docs/formats/spectralis-capsule.md`.
- **`.spectral` album worlds** — multi-track, with an HTML world page and/or a sandboxed
  Wasm/wgpu 3D runtime. `docs/formats/spectral-album-world.md` +
  `spectralis-world-sdk/docs/host-imports.md`.
- **ID3-embedded visualizer modules** — `DELTA_MODULE_`/`DELTA_DATA_`/`DELTA_BIN_` frames
  embedded straight into an MP3. `docs/formats/metadata-embedding.md`.

## What this is and isn't

This server is **plumbing, not a content generator**. It scaffolds starter files already wired
to the real `window.spectral` bridge / CSS-var / manifest contract, validates manifests against
the documented rules, and handles zipping/signing/self-verification — but the actual creative
HTML/JS/Rust content is written by whichever agent is driving it, as normal file edits, same as
how real capsules ("The Line", "Trendsetter") have always been built.

It is also **100% local and offline** — no network calls, no CDN interaction, ever. Every
signing operation only proves "signed with this local key"; getting a capsule/world trusted by
someone else's real Spectralis install additionally requires registering that key's public half
with the CDN's creator-key infrastructure (`docs/cdn-contract.md`) by hand. This server cannot
do that part and never tries to.

## Tools

**Discovery / validation** (read-only)
- `list_capabilities` — the capability table from `docs/cdn-contract.md`.
- `list_signing_keys` — local `~/.spectralis/keys/*.pem` keys (fingerprint only, never private bytes).
- `validate_capsule_manifest` / `validate_album_manifest` — schema + capability checks, plus
  referenced-file existence when given a folder.

**Scaffolding** (creates starter files; you write the creative content afterward)
- `new_capsule_project` — a `metadata/<song>/`-style capsule folder.
- `new_album_world_project` — a `.spectral` album-world folder.
- `scaffold_wasm_world` — a Wasm/wgpu crate from spectralis-world-sdk's `rust-minimal` template.

**Packing / signing**
- `pack_capsule` — signs a capsule folder into `.spectralis` (auto-fills audio sha256/duration
  and signature keyId/fingerprint if placeholders).
- `pack_album_world` — signs an album-world folder into `.spectral`.
- `world_to_lite` — derives a Wasm-stripped Lite bundle from an already-signed `.spectral`.
- `embed_id3_module` / `read_embedded_modules` — ID3 module embedding + round-trip readback.

**Key management**
- `generate_signing_key` — mints a new local Ed25519 identity (refuses to overwrite). Read its
  tool description before using it — it only creates a *local* key.

## Requirements

Already installed in this environment: `mcp` (the Python MCP SDK), `cryptography`, `mutagen`.
No project-specific virtualenv — these were already on the system Python used by the existing
`tools/*.py` / `metadata/*/pack_*.py` scripts.

By default this server expects `spectralis-world-sdk` checked out at
`~/Documents/spectralis-world-sdk`. Override with the `SPECTRALIS_WORLD_SDK_PATH` environment
variable if it lives elsewhere. (Capsule-only tools don't need it at all — it's only imported
by `pack_album_world`, `world_to_lite`, `scaffold_wasm_world`, and the `world-sdk-*` doc
resources.)

## Registration

Registered user-scoped (private to this machine, not committed to the repo):

```sh
claude mcp add -s user spectral-mcp -- python "<repo>/spectral-mcp/server.py"
```

Check it's connected with `claude mcp get spectral-mcp`. To remove: `claude mcp remove spectral-mcp -s user`.

## Files

```
server.py              entrypoint — registers all tools/resources, stdio transport
formats.py              capability table, size limits, doc-resource paths (mirrors the docs)
capsule.py              .spectralis scaffolding/validation/signing (imports tools/pack_capsule.py)
album_world.py          .spectral scaffolding/validation/signing/lite (imports pack_world.py + world_to_lite.py)
embedded_module.py      ID3 DELTA_MODULE_/DELTA_DATA_/DELTA_BIN_ embedding (mutagen)
keys.py                 Ed25519 key discovery/generation under ~/.spectralis/keys/
wasm_scaffold.py         copies spectralis-world-sdk's rust-minimal template
templates/
  visualizer_starter.html    starter capsule visualizer (real window.spectral/CSS-var wiring)
  album_world_starter.html   starter album-world page (real onReady/postMessage wiring)
```

The actual `.spectralis` signer lives in the main repo's `tools/pack_capsule.py` — a plain,
standalone script usable on its own (`python tools/pack_capsule.py my-capsule/ --key ...`),
exactly like the existing per-song `pack_<song>.py` scripts. `capsule.py` imports its functions
directly rather than re-implementing them, so there's exactly one place that knows the SPCC
binary layout.
