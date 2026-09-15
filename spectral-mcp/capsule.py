"""Single-track `.spectralis` capsule support: scaffolding, validation, and signing.

The zip/sign/self-verify logic itself lives in the top-level `tools/pack_capsule.py` (a plain,
standalone script usable on its own, exactly like the existing per-song pack_<song>.py scripts)
— this module imports its functions directly rather than re-implementing them, so there's
exactly one place that knows the SPCC binary layout.
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

import formats
import keys as keys_mod

_TEMPLATES_DIR = Path(__file__).resolve().parent / "templates"


def _load_module(path: Path, name: str) -> ModuleType:
    if not path.exists():
        raise FileNotFoundError(f"{path} not found")
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)  # module.__name__ != "__main__", so its CLI guard is inert
    return module


_pc = _load_module(formats.REPO_ROOT / "tools" / "pack_capsule.py", "spectral_mcp_pack_capsule")


def _slugify(title: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")
    return slug or "untitled"


# ---- validation -------------------------------------------------------------------------


def validate_capsule_manifest(manifest: dict, folder: Path | None = None) -> dict:
    """Checks a spectralis-capsule manifest against docs/formats/spectralis-capsule.md's
    required fields and docs/cdn-contract.md's known capability list. Pass `folder` to also
    check that every file the manifest references actually exists there."""
    errors: list[str] = []
    warnings: list[str] = []

    try:
        _pc.validate_manifest(manifest)
    except _pc.PackError as exc:
        errors.append(str(exc))

    for cap in manifest.get("capabilities", []) or []:
        if cap not in formats.CAPABILITIES:
            warnings.append(
                f"unknown capability {cap!r} — not in docs/cdn-contract.md's table "
                "(typo, or a capability newer than this server knows about)"
            )

    if folder is not None:
        warnings.extend(_pc.check_referenced_files_exist(folder, manifest))

    return {"ok": not errors, "errors": errors, "warnings": warnings}


# ---- scaffolding -------------------------------------------------------------------------


def new_capsule_project(dest_folder: str, title: str, artist: str, audio_path: str) -> dict:
    """Scaffolds a metadata/<song>/-style capsule folder: copies the audio in, computes its
    sha256/duration, writes a manifest.json + blank reactive.json, and drops in a starter
    visualizer.html already wired to the real window.spectral / CSS-var contract. The creative
    content of the visualizer is left for you to write — see the comment block at the top of
    the generated file."""
    dest = Path(dest_folder).expanduser().resolve()
    if dest.exists() and any(dest.iterdir()):
        raise FileExistsError(f"{dest} already exists and is not empty")

    audio_src = Path(audio_path).expanduser().resolve()
    if not audio_src.exists():
        raise FileNotFoundError(str(audio_src))

    dest.mkdir(parents=True, exist_ok=True)
    slug = _slugify(title)

    audio_dir = dest / "audio"
    audio_dir.mkdir(parents=True, exist_ok=True)
    audio_dest_name = f"{slug}{audio_src.suffix}"
    shutil.copy2(audio_src, audio_dir / audio_dest_name)

    sha256, duration = _pc.compute_audio_meta(audio_dir / audio_dest_name)

    manifest = {
        "format": "spectralis-capsule",
        "formatVersion": 3,
        "id": f"{slug}-{datetime.date.today().isoformat()}",
        "title": title,
        "artist": artist,
        "release": {"year": datetime.date.today().year, "credits": []},
        "audio": {
            "entry": f"audio/{audio_dest_name}",
            "sha256": sha256,
            "durationSeconds": duration or 0,
        },
        "assets": {"images": [], "fonts": [], "videos": [], "data": []},
        "visualizers": [
            {
                "id": f"{slug}_viz",
                "type": "html",
                "runtime": "html",
                "moduleEntry": "assets/data/module.json",
                "binaryEntry": "assets/html/visualizer.html",
                "binaryAssets": {},
                "dataAssets": {},
            }
        ],
        "timeline": [{"entry": "reactive.json", "type": "spectralis-track-reactive", "version": 3}],
        "suppressAppLyrics": False,
        "capabilities": ["webview.localContent"],
        "signature": {"keyId": "FILL-IN", "fingerprint": "FILL-IN", "algorithm": "Ed25519", "value": "capsule-header"},
    }
    (dest / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    reactive = {"format": "spectralis-track-reactive", "formatVersion": 3, "sections": [], "timeline": []}
    (dest / "reactive.json").write_text(json.dumps(reactive, indent=2), encoding="utf-8")

    (dest / "assets" / "data").mkdir(parents=True, exist_ok=True)
    module_json = {
        "id": f"{slug}_viz",
        "type": "html",
        "runtime": "html",
        "entry": "",
        "version": "1.0.0",
        "binaryRef": f"{slug}_html",
        "width": 1920,
        "height": 1080,
        "dataRefs": {},
    }
    (dest / "assets" / "data" / "module.json").write_text(json.dumps(module_json, indent=2), encoding="utf-8")

    html_dir = dest / "assets" / "html"
    html_dir.mkdir(parents=True, exist_ok=True)
    starter = (_TEMPLATES_DIR / "visualizer_starter.html").read_text(encoding="utf-8")
    starter = starter.replace("{{TITLE}}", title)
    (html_dir / "visualizer.html").write_text(starter, encoding="utf-8")

    return {
        "folder": str(dest),
        "manifest_path": str(dest / "manifest.json"),
        "visualizer_path": str(html_dir / "visualizer.html"),
        "audio_sha256": sha256,
        "duration_seconds": duration,
        "next_steps": [
            "edit assets/html/visualizer.html with the real creative visuals",
            "add cover art / lyrics if wanted (assets.images / assets.data, dataAssets.cover, etc.)",
            "pick a signing key with list_signing_keys (or make one with generate_signing_key)",
            "call pack_capsule to sign it",
        ],
    }


# ---- packing -----------------------------------------------------------------------------


def pack_capsule(folder: str, key_path: str | None = None, output_path: str | None = None) -> dict:
    """Signs a capsule folder into a `.spectralis` file. Auto-fills manifest.audio.sha256 /
    durationSeconds from the real audio file if they're missing or a placeholder (errors out
    instead if a present sha256 doesn't match — that means the wrong file is in the folder),
    and fills manifest.signature.keyId/fingerprint from the resolved signing key if those are
    placeholders too. Self-verifies the written file before returning.

    Local, offline signing only — this does not register anything with the CDN, so the
    resulting capsule will only be trusted by installs that already trust this key
    (see generate_signing_key's docstring)."""
    folder_p = Path(folder).expanduser().resolve()
    if not folder_p.is_dir():
        raise NotADirectoryError(f"{folder_p} is not a directory")

    manifest_path = folder_p / "manifest.json"
    if not manifest_path.exists():
        raise FileNotFoundError(f"{manifest_path} not found")

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    _pc.validate_manifest(manifest)
    warnings = _pc.check_referenced_files_exist(folder_p, manifest)

    key_file = keys_mod.resolve_key(key_path)
    priv = keys_mod.load_key(key_file)
    fingerprint, _pub = keys_mod.identity_of(priv)
    key_id = key_file.stem

    _pc.autofill_audio_fields(folder_p, manifest, verbose=False)

    sig = manifest.setdefault("signature", {})
    if sig.get("keyId") in (None, "", "FILL-IN"):
        sig["keyId"] = key_id
    if sig.get("fingerprint") in (None, "", "FILL-IN"):
        sig["fingerprint"] = fingerprint
    sig.setdefault("algorithm", "Ed25519")
    sig.setdefault("value", "capsule-header")

    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    out_path = Path(output_path).expanduser().resolve() if output_path else folder_p / f"{manifest['id']}.spectralis"

    # These functions print progress — never let that hit real stdout, it's the MCP stdio
    # transport's JSON-RPC channel.
    log = io.StringIO()
    with contextlib.redirect_stdout(log):
        payload = _pc.zip_folder(folder_p)
        _pc.sign_and_write(payload, priv, out_path)
        _pc.self_verify(out_path)

    return {
        "output_path": str(out_path),
        "size_bytes": out_path.stat().st_size,
        "key_id": key_id,
        "fingerprint": fingerprint,
        "warnings": warnings,
        "log": log.getvalue(),
    }
