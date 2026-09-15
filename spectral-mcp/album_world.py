"""Multi-track `.spectral` album world support: scaffolding, validation, signing, and Lite
derivation.

Signing logic is not re-implemented here — this module imports the functions straight out of
spectralis-world-sdk's `tools/pack_world.py` (SPAC signer, "author a new world") and this
repo's own `tools/world_to_lite.py` (strip the Wasm payload from an already-signed package),
so there's exactly one implementation of each, matching how those two tools already describe
their own relationship in their docstrings.
"""

from __future__ import annotations

import contextlib
import datetime
import importlib.util
import io
import json
import re
import shutil
from pathlib import Path
from types import ModuleType

from cryptography.hazmat.primitives import serialization

import formats
import keys as keys_mod

_TEMPLATES_DIR = Path(__file__).resolve().parent / "templates"


def _load_module(path: Path, name: str) -> ModuleType:
    if not path.exists():
        raise FileNotFoundError(f"{path} not found")
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


_pack_world = _load_module(formats.WORLD_SDK_ROOT / "tools" / "pack_world.py", "spectral_mcp_pack_world")
_world_to_lite = _load_module(formats.REPO_ROOT / "tools" / "world_to_lite.py", "spectral_mcp_world_to_lite")
# Reused only for its audio sha256/duration helper — see capsule.py for the same load.
_pack_capsule = _load_module(formats.REPO_ROOT / "tools" / "pack_capsule.py", "spectral_mcp_pack_capsule_for_albums")


def _slugify(title: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")
    return slug or "untitled"


# ---- validation -------------------------------------------------------------------------


def validate_album_manifest(manifest: dict, folder: Path | None = None) -> dict:
    """Checks a spectralis-album manifest against docs/formats/spectral-album-world.md's
    required fields (including the album.world / worlds.wasm3d capability pairing rules) and
    docs/cdn-contract.md's known capability list. Pass `folder` to also check that every file
    the manifest references actually exists there."""
    errors: list[str] = []
    warnings: list[str] = []

    try:
        _pack_world.validate_manifest(manifest)
    except _pack_world.PackError as exc:
        errors.append(str(exc))

    for cap in manifest.get("capabilities", []) or []:
        if cap not in formats.CAPABILITIES:
            warnings.append(
                f"unknown capability {cap!r} — not in docs/cdn-contract.md's table "
                "(typo, or a capability newer than this server knows about)"
            )

    if folder is not None:
        warnings.extend(_pack_world.check_referenced_files_exist(folder, manifest))

    return {"ok": not errors, "errors": errors, "warnings": warnings}


# ---- scaffolding -------------------------------------------------------------------------


def new_album_world_project(dest_folder: str, title: str, artist: str, tracks: list[dict]) -> dict:
    """Scaffolds a `.spectral` album-world folder: per-track subfolders with audio copied in
    and sha256/duration computed, a manifest.json (spectralis-album v1, `album.world`
    capability), and a starter world/index.html wired to the real
    onReady/onTrackChanged/onPlaybackFrame/postMessage contract.

    `tracks` is a list of {"id", "title", "audio_path", "artist"?} dicts, at least one
    required. The world page's creative content is left for you to write — see the comment
    block at the top of the generated world/index.html. For a 3D Wasm/wgpu world alongside it,
    use scaffold_wasm_world separately and wire the result into manifest.world.wasmEntry."""
    if not tracks:
        raise ValueError("at least one track is required")

    dest = Path(dest_folder).expanduser().resolve()
    if dest.exists() and any(dest.iterdir()):
        raise FileExistsError(f"{dest} already exists and is not empty")
    dest.mkdir(parents=True, exist_ok=True)

    manifest_tracks = []
    for track in tracks:
        tid = track.get("id")
        if not tid:
            raise ValueError(f"track missing required 'id': {track!r}")
        audio_src = Path(track["audio_path"]).expanduser().resolve()
        if not audio_src.exists():
            raise FileNotFoundError(str(audio_src))

        track_dir = dest / "tracks" / tid
        track_dir.mkdir(parents=True, exist_ok=True)
        audio_dest_name = f"audio{audio_src.suffix}"
        shutil.copy2(audio_src, track_dir / audio_dest_name)

        sha256, duration = _pack_capsule.compute_audio_meta(track_dir / audio_dest_name)

        manifest_tracks.append(
            {
                "id": tid,
                "title": track.get("title", tid),
                "artist": track.get("artist", artist),
                "audio": {
                    "entry": f"tracks/{tid}/{audio_dest_name}",
                    "sha256": sha256,
                    "durationSeconds": duration or 0,
                },
                "assets": {"images": [], "data": []},
                "visualizers": [],
                "timeline": [],
                "suppressAppLyrics": False,
            }
        )

    slug = _slugify(title)
    world_dir = dest / "world"
    world_dir.mkdir(parents=True, exist_ok=True)
    starter = (_TEMPLATES_DIR / "album_world_starter.html").read_text(encoding="utf-8")
    starter = starter.replace("{{TITLE}}", title)
    (world_dir / "index.html").write_text(starter, encoding="utf-8")

    manifest = {
        "format": "spectralis-album",
        "formatVersion": 1,
        "id": f"{slug}-{datetime.date.today().isoformat()}",
        "title": title,
        "artist": artist,
        "release": {"year": datetime.date.today().year, "credits": []},
        "capabilities": ["webview.localContent", "album.world"],
        "world": {"entry": "world/index.html", "binaryAssets": {}, "dataAssets": {}},
        "tracks": manifest_tracks,
        "signature": {"keyId": "FILL-IN", "fingerprint": "FILL-IN", "algorithm": "Ed25519", "value": "capsule-header"},
    }
    (dest / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    return {
        "folder": str(dest),
        "manifest_path": str(dest / "manifest.json"),
        "world_entry": str(world_dir / "index.html"),
        "track_count": len(manifest_tracks),
        "next_steps": [
            "edit world/index.html with the real world experience",
            "optionally scaffold_wasm_world for a 3D runtime, then set manifest.world.wasmEntry "
            "and add the worlds.wasm3d capability",
            "pick a signing key with list_signing_keys (or make one with generate_signing_key)",
            "call pack_album_world to sign it",
        ],
    }


# ---- packing / derivation ------------------------------------------------------------------


def pack_album_world(folder: str, key_path: str | None = None, output_path: str | None = None) -> dict:
    """Signs an album-world folder into a `.spectral` file via spectralis-world-sdk's
    pack_world.py. Fills manifest.signature.keyId/fingerprint from the resolved signing key if
    those are placeholders. Local, offline signing only — see pack_capsule's docstring for why
    that means the result is only trusted by installs that already trust this key."""
    folder_p = Path(folder).expanduser().resolve()
    if not folder_p.is_dir():
        raise NotADirectoryError(f"{folder_p} is not a directory")

    manifest_path = folder_p / "manifest.json"
    if not manifest_path.exists():
        raise FileNotFoundError(f"{manifest_path} not found")

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    _pack_world.validate_manifest(manifest)
    warnings = _pack_world.check_referenced_files_exist(folder_p, manifest)

    key_file = keys_mod.resolve_key(key_path)
    priv = keys_mod.load_key(key_file)
    fingerprint, _pub = keys_mod.identity_of(priv)
    key_id = key_file.stem

    if "signature" in manifest:
        sig = manifest["signature"]
        if sig.get("keyId") in (None, "", "FILL-IN"):
            sig["keyId"] = key_id
        if sig.get("fingerprint") in (None, "", "FILL-IN"):
            sig["fingerprint"] = fingerprint
        manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    out_path = Path(output_path).expanduser().resolve() if output_path else folder_p.parent / f"{manifest['id']}.spectral"

    log = io.StringIO()
    with contextlib.redirect_stdout(log):
        payload = _pack_world.zip_folder(folder_p)
        _pack_world.sign_and_write(payload, priv, out_path)
        _pack_world.self_verify(out_path)

    return {
        "output_path": str(out_path),
        "size_bytes": out_path.stat().st_size,
        "key_id": key_id,
        "fingerprint": fingerprint,
        "warnings": warnings,
        "log": log.getvalue(),
    }


def world_to_lite(input_path: str, output_path: str | None = None, key_path: str | None = None) -> dict:
    """Derives a Lite bundle (Wasm/wgpu world stripped, everything else byte-for-byte) from an
    already-signed `.spectral` file via this repo's tools/world_to_lite.py. Re-signs with the
    same creator key as the source — that key must be available locally (auto-discovered under
    ~/.spectralis/keys/ unless key_path is given)."""
    in_p = Path(input_path).expanduser().resolve()
    if not in_p.exists():
        raise FileNotFoundError(str(in_p))

    out_p = (
        Path(output_path).expanduser().resolve()
        if output_path
        else in_p.with_name(in_p.stem + "-lite" + in_p.suffix)
    )

    log = io.StringIO()
    with contextlib.redirect_stdout(log):
        pubkey_bytes, _sig, payload = _world_to_lite.read_spac(in_p)

        if key_path:
            priv = keys_mod.load_key(Path(key_path).expanduser())
            candidate_pub = priv.public_key().public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)
            if candidate_pub != pubkey_bytes:
                raise ValueError(f"{key_path} does not match the source package's signing key")
        else:
            priv = _world_to_lite.load_private_key_matching(pubkey_bytes, _world_to_lite.find_candidate_keys())

        lite_payload = _world_to_lite.strip_wasm_world(payload, verbose=True)
        _world_to_lite.sign_and_write(lite_payload, priv, out_p)
        _world_to_lite.self_verify(out_p)

    return {"output_path": str(out_p), "size_bytes": out_p.stat().st_size, "log": log.getvalue()}
