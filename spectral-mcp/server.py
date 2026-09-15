#!/usr/bin/env python3
"""Spectral MCP server — lets agentic tools scaffold, validate, sign, and embed Spectralis
content: `.spectralis` capsules, `.spectral` album worlds (HTML and/or Wasm/wgpu 3D), and
ID3-embedded visualizer modules.

This server is plumbing, not a content generator: it wires up the real
window.spectral/CSS-var/manifest contract correctly and handles signing/packaging, but the
creative HTML/JS/Rust content is written by whatever agent is driving it, as normal file
edits. See spectral-mcp/README.md for the full tool list and what each one does and doesn't do.

Everything here is 100% local/offline — no network calls, no CDN interaction. Run with:
    python server.py
"""

from __future__ import annotations

import sys
from pathlib import Path

# This directory's own name has a hyphen (spectral-mcp), which isn't a valid Python package
# identifier — so these are plain sibling-module imports via sys.path, not a package import.
sys.path.insert(0, str(Path(__file__).resolve().parent))

import album_world  # noqa: E402
import capsule  # noqa: E402
import embedded_module  # noqa: E402
import formats  # noqa: E402
import keys as keys_mod  # noqa: E402
import wasm_scaffold  # noqa: E402

from mcp.server.mcpserver import MCPServer  # noqa: E402

mcp = MCPServer(
    name="spectral-mcp",
    title="Spectral MCP",
    description="Scaffold, validate, sign, and embed Spectralis capsules/album-worlds/visualizers.",
    version="0.1.0",
)


# ---- resources: serve the format docs verbatim, read fresh every call --------------------


def _register_doc_resources() -> None:
    for uri, path in formats.DOC_RESOURCES:
        def _make_reader(doc_path: Path):
            def _read() -> str:
                if not doc_path.exists():
                    return f"(missing: {doc_path})"
                return doc_path.read_text(encoding="utf-8")

            return _read

        mcp.resource(uri, name=path.name, description=f"Spectralis format doc: {path.name}", mime_type="text/markdown")(
            _make_reader(path)
        )


_register_doc_resources()


# ---- tools: discovery / validation ---------------------------------------------------------


@mcp.tool(description="Lists Spectralis capability strings (from docs/cdn-contract.md) usable in a capsule/album manifest's `capabilities` array, with what each one does. Note: requesting a capability only makes it *signable* locally — real installs still check the signing key against a CDN allowlist, which this server never touches.")
def list_capabilities() -> dict:
    return {"capabilities": formats.CAPABILITIES}


@mcp.tool(description="Lists local Ed25519 signing keys under ~/.spectralis/keys/*.pem (file name, key id, fingerprint only — never private key bytes).")
def list_signing_keys() -> dict:
    return {"keys": [k.__dict__ for k in keys_mod.list_keys()]}


@mcp.tool(description="Mints a brand-new local Ed25519 signing key at ~/.spectralis/keys/<key_id>.pem. Refuses to overwrite an existing key. This is LOCAL ONLY: a capsule/world signed with it will only be trusted by installs that already trust this key by fingerprint — getting it trusted by real Spectralis installs requires separately registering the public key with the CDN's creator-key infrastructure (docs/cdn-contract.md), which this tool does not and cannot do.")
def generate_signing_key(key_id: str) -> dict:
    info = keys_mod.generate_key(key_id)
    return info.__dict__


@mcp.tool(description="Validates a spectralis-capsule manifest object (required fields, format/version, known capabilities). Pass folder to also check every file the manifest references actually exists there.")
def validate_capsule_manifest(manifest: dict, folder: str | None = None) -> dict:
    folder_path = Path(folder).expanduser().resolve() if folder else None
    return capsule.validate_capsule_manifest(manifest, folder_path)


@mcp.tool(description="Validates a spectralis-album manifest object (required fields, format/version, album.world/worlds.wasm3d capability pairing, known capabilities). Pass folder to also check every file the manifest references actually exists there.")
def validate_album_manifest(manifest: dict, folder: str | None = None) -> dict:
    folder_path = Path(folder).expanduser().resolve() if folder else None
    return album_world.validate_album_manifest(manifest, folder_path)


# ---- tools: scaffolding ---------------------------------------------------------------------


@mcp.tool(description="Scaffolds a new single-track capsule folder: copies the audio in, computes its sha256/duration, writes manifest.json + a blank reactive.json, and drops in a starter visualizer.html already wired to the real window.spectral/CSS-var contract (--audio-time/--audio-peak/--audio-rms, .audio-active, delta-asset:/delta-data-json: refs, ?t= fixed-time preview). Edit the visualizer's creative content afterward as a normal file edit, then call pack_capsule.")
def new_capsule_project(dest_folder: str, title: str, artist: str, audio_path: str) -> dict:
    return capsule.new_capsule_project(dest_folder, title, artist, audio_path)


@mcp.tool(description="Scaffolds a new `.spectral` album-world folder: per-track subfolders with audio copied in, manifest.json (spectralis-album v1, album.world capability), and a starter world/index.html wired to the real onReady/onTrackChanged/onPlaybackFrame/postMessage contract. tracks is a list of {id, title, audio_path, artist?} objects. Edit the world page's creative content afterward, then call pack_album_world.")
def new_album_world_project(dest_folder: str, title: str, artist: str, tracks: list[dict]) -> dict:
    return album_world.new_album_world_project(dest_folder, title, artist, tracks)


@mcp.tool(description="Scaffolds a new Wasm/wgpu 3D album-world crate from spectralis-world-sdk's rust-minimal template, renamed to crate_name. Build with `cargo build --target wasm32-unknown-unknown --release`, then point an album manifest's world.wasmEntry at the output .wasm and add the worlds.wasm3d capability. Current real limits: no scene-graph, one flat vertex/index buffer via submit_geometry, host owns the only camera — see spectral://docs/world-sdk-host-imports.")
def scaffold_wasm_world(dest_folder: str, crate_name: str) -> dict:
    return wasm_scaffold.scaffold_wasm_world(dest_folder, crate_name)


# ---- tools: packing / signing ----------------------------------------------------------------


@mcp.tool(description="Signs a capsule folder (from new_capsule_project, or any manifest.json-rooted folder matching docs/formats/spectralis-capsule.md) into a `.spectralis` file. Auto-fills manifest.audio.sha256/durationSeconds and manifest.signature.keyId/fingerprint if they're missing/placeholders (errors if a present sha256 doesn't match the actual audio file). key_path is optional if you have exactly one local signing key. Local/offline signing only — see generate_signing_key's description for what that does and doesn't mean.")
def pack_capsule(folder: str, key_path: str | None = None, output_path: str | None = None) -> dict:
    return capsule.pack_capsule(folder, key_path, output_path)


@mcp.tool(description="Signs an album-world folder (from new_album_world_project) into a `.spectral` file. Auto-fills manifest.signature.keyId/fingerprint if placeholders. key_path is optional if you have exactly one local signing key. Local/offline signing only.")
def pack_album_world(folder: str, key_path: str | None = None, output_path: str | None = None) -> dict:
    return album_world.pack_album_world(folder, key_path, output_path)


@mcp.tool(description="Derives a Lite bundle (Wasm/wgpu 3D world stripped, everything else byte-for-byte) from an already-signed `.spectral` file. Re-signs with the same creator key as the source, auto-discovered under ~/.spectralis/keys/ unless key_path is given.")
def world_to_lite(input_path: str, output_path: str | None = None, key_path: str | None = None) -> dict:
    return album_world.world_to_lite(input_path, output_path, key_path)


@mcp.tool(description="Embeds a DELTA_MODULE_/DELTA_DATA_/DELTA_BIN_ ID3v2 module directly into an MP3 (docs/formats/metadata-embedding.md), enforcing its size limits before writing (64KB/data block, 256KB non-video binary, 16MB video). module describes the module ({id, type, runtime, binaryRef, ...}); binary_path is the local file embedded under module.binaryRef; data_blocks optionally maps binding name -> text/JSON string for module.dataRefs. NOTE: the app can currently execute `html` modules but has no in-process WASM executor yet (docs/blockers.md) — a `wasm` module will embed and round-trip but won't render today.")
def embed_id3_module(mp3_path: str, module: dict, binary_path: str, data_blocks: dict[str, str] | None = None) -> dict:
    return embedded_module.embed_module(mp3_path, module, binary_path, data_blocks)


@mcp.tool(description="Reads back every DELTA_MODULE_* frame's JSON descriptor from an MP3, to confirm what's embedded or verify an embed_id3_module round-trip.")
def read_embedded_modules(mp3_path: str) -> dict:
    return {"modules": embedded_module.read_modules(mp3_path)}


if __name__ == "__main__":
    mcp.run("stdio")
