"""Shared format constants for the Spectral MCP server.

These mirror docs/formats/*.md and docs/cdn-contract.md, which remain the source of truth —
this module exists so tool code has a plain Python home for the same numbers/tables instead of
re-parsing markdown at runtime. Keep in sync by hand when those docs change; each constant below
names the doc section it mirrors.
"""

from __future__ import annotations

import os
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent

# spectralis-world-sdk lives in a sibling repo, not nested under this one. Override with
# SPECTRALIS_WORLD_SDK_PATH if it's checked out somewhere else.
WORLD_SDK_ROOT = Path(
    os.environ.get("SPECTRALIS_WORLD_SDK_PATH", str(Path.home() / "Documents" / "spectralis-world-sdk"))
)

KEYS_DIR = Path.home() / ".spectralis" / "keys"

# docs/cdn-contract.md, "Allowed Capabilities" table.
CAPABILITIES: dict[str, str] = {
    "app.theme.deepControl": "Capsule can set deep shell theme overrides",
    "app.layout.deepControl": "Capsule can modify shell layout",
    "app.chrome.effects": "Capsule can apply window chrome effects",
    "visualizer.multiLayer": "Capsule can compose multiple visualizer layers",
    "visualizer.wasm": "Capsule can embed a WASM visualizer module",
    "visualizer.shaderPack": "Capsule can supply shader packs",
    "webview.localContent": "Capsule can load local HTML content in WebView2",
    "webview.networkAccess": "Capsule's WebView content may access the network",
    "album.world": "Capsule may include an interactive album world (required for .spectral)",
    "sharedPlay.hostCapsule": "Capsule can be hosted via Shared Play",
    "sharedPlay.packageUpload": "Capsule assets may be uploaded for Shared Play",
    "timeline.appControl": "Capsule's reactive timeline may issue app control events",
    "presence.richPresence": "Embedded HTML may override Discord rich presence text while the capsule plays",
    "worlds.wasm3d": "Capsule may include a sandboxed Wasm/wgpu 3D album world",
    "audio.dspPreset": "Embedded HTML/Wasm content may register a whole-rack DSP preset while active",
}

# NOTE: requesting a capability in a manifest only makes it *signable* locally. A real
# Spectralis install checks the capsule's signing-key fingerprint against a CDN-hosted
# allowlist (docs/cdn-contract.md, "Creator Key Metadata") before honouring any of them. This
# server never talks to that CDN — a freshly generated key (see keys.generate_key) can sign
# anything, but nobody else's install will trust it until its public key is registered there
# by hand. That registration step is out of scope for every tool here.

# docs/formats/metadata-embedding.md size limits.
DATA_BLOCK_MAX = 64 * 1024
BINARY_ASSET_MAX = 256 * 1024
VIDEO_BINARY_MAX = 16 * 1024 * 1024

# docs/formats/metadata-embedding.md module type/runtime enums.
MODULE_TYPES = {"visualizer", "html", "markdown", "video"}
MODULE_RUNTIMES = {"wasm", "html", "markdown", "h264", "vp9", "av1", "h265"}
MODULE_ID_MAX_LEN = 64

# spectralis-world-sdk/docs/host-imports.md submit_geometry caps (mirrors WasmWorldHost.cs).
WASM_GEOMETRY_MAX_VERTICES = 65536
WASM_GEOMETRY_MAX_INDICES = 300000

# docs/formats/reactive-timeline.md enums.
REACTIVE_TARGETS = {"theme", "visualizer", "lyrics", "shader"}
REACTIVE_ACTIONS = {"set", "transition", "reset"}
REACTIVE_EASINGS = {"linear", "incubic", "outcubic", "inoutcubic", "insine", "outsine"}

# Docs the MCP exposes as resources — (uri, absolute path). Read fresh on every request, no
# caching, so they can never drift from what's actually on disk.
DOC_RESOURCES: list[tuple[str, Path]] = [
    ("spectral://docs/capsule-format", REPO_ROOT / "docs" / "formats" / "spectralis-capsule.md"),
    ("spectral://docs/album-world-format", REPO_ROOT / "docs" / "formats" / "spectral-album-world.md"),
    ("spectral://docs/reactive-timeline", REPO_ROOT / "docs" / "formats" / "reactive-timeline.md"),
    ("spectral://docs/metadata-embedding", REPO_ROOT / "docs" / "formats" / "metadata-embedding.md"),
    ("spectral://docs/cdn-contract", REPO_ROOT / "docs" / "cdn-contract.md"),
    ("spectral://docs/world-sdk-readme", WORLD_SDK_ROOT / "README.md"),
    ("spectral://docs/world-sdk-host-imports", WORLD_SDK_ROOT / "docs" / "host-imports.md"),
]
