#!/usr/bin/env python3
"""
pack_capsule.py — package a capsule folder into a signed .spectralis file (SPCC v3).

Generic sibling of world_to_lite.py in this repo and spectralis-world-sdk's pack_world.py, but
for the single-track capsule format described in docs/formats/spectralis-capsule.md. Every
existing capsule (metadata/<song>/) was packed by a bespoke pack_<song>.py hand-written per
song (see metadata/biding-our-time/pack_biding.py for the pattern this generalizes); this is
one reusable tool instead.

Reads a folder containing manifest.json (spectralis-capsule format v3) plus whatever files it
references, auto-fills manifest.audio.sha256/durationSeconds from the real audio file when
they're missing or a known placeholder (removes the class of hand-copied hash mismatch bugs
the per-song scripts guard against manually), zips, and signs with the given Ed25519 creator
key using the exact binary container real capsules use (SPCC magic + header + signed zip
payload).

Usage:
    python pack_capsule.py <capsule_folder> --key path/to/key.pem [-o output.spectralis]

If you don't have a creator key yet, generate one (a LOCAL key only — getting a capsule
trusted by real Spectralis installs also requires the public key to be registered with the
CDN's creator-key infrastructure the app's CreatorTrustStore checks against; see
docs/cdn-contract.md — that registration step is outside this tool's scope):
    python -c "
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
    from cryptography.hazmat.primitives import serialization
    k = Ed25519PrivateKey.generate()
    open('my-key.pem', 'wb').write(k.private_bytes(
        serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
    "
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
import zipfile
from pathlib import Path

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey

MAGIC = b"SPCC"
FORMAT_VERSION = 3
HEADER_LEN = 104  # 4 magic + 4 version + 32 pubkey + 64 signature
REQUIRED_MANIFEST_FIELDS = ("format", "formatVersion", "id", "title", "artist", "audio", "signature", "capabilities")
PLACEHOLDER_VALUES = {"", "FILL-IN", "FILL-IN-VIA-PACK-SCRIPT"}


class PackError(ValueError):
    pass


def validate_manifest(manifest: dict) -> None:
    missing = [f for f in REQUIRED_MANIFEST_FIELDS if f not in manifest]
    if missing:
        raise PackError(f"manifest.json missing required field(s): {', '.join(missing)}")
    if manifest["format"] != "spectralis-capsule":
        raise PackError(f"manifest.json: format must be 'spectralis-capsule', got {manifest['format']!r}")
    if manifest["formatVersion"] != FORMAT_VERSION:
        raise PackError(f"manifest.json: formatVersion must be {FORMAT_VERSION}, got {manifest['formatVersion']!r}")
    if not manifest.get("audio", {}).get("entry"):
        raise PackError("manifest.json: audio.entry is required")


def check_referenced_files_exist(folder: Path, manifest: dict) -> list[str]:
    """Returns a list of warnings (not fatal) for manifest-referenced paths that don't exist."""
    warnings: list[str] = []

    def check(rel_path: str, label: str) -> None:
        if rel_path and not (folder / rel_path).exists():
            warnings.append(f"{label} references missing file: {rel_path}")

    check(manifest.get("audio", {}).get("entry", ""), "audio.entry")

    assets = manifest.get("assets") or {}
    for kind in ("images", "fonts", "videos", "data"):
        for rel in assets.get(kind, []) or []:
            check(rel, f"assets.{kind}")

    for viz in manifest.get("visualizers", []) or []:
        vid = viz.get("id", "?")
        check(viz.get("moduleEntry", ""), f"visualizers[{vid}].moduleEntry")
        check(viz.get("binaryEntry", ""), f"visualizers[{vid}].binaryEntry")
        for key, rel in (viz.get("binaryAssets") or {}).items():
            check(rel, f"visualizers[{vid}].binaryAssets.{key}")
        for key, rel in (viz.get("dataAssets") or {}).items():
            check(rel, f"visualizers[{vid}].dataAssets.{key}")

    for tl in manifest.get("timeline", []) or []:
        check(tl.get("entry", ""), "timeline.entry")

    story = manifest.get("story") or {}
    check(story.get("entry", ""), "story.entry")
    for key, rel in (story.get("binaryAssets") or {}).items():
        check(rel, f"story.binaryAssets.{key}")
    for key, rel in (story.get("dataAssets") or {}).items():
        check(rel, f"story.dataAssets.{key}")

    return warnings


def compute_audio_meta(path: Path) -> tuple[str, float | None]:
    """Returns (sha256_hex, duration_seconds_or_None) for the audio file at path."""
    data = path.read_bytes()
    sha256 = hashlib.sha256(data).hexdigest()
    duration: float | None = None
    try:
        import mutagen

        info = mutagen.File(path)
        if info is not None and getattr(info, "info", None) is not None:
            duration = round(float(info.info.length), 3)
    except Exception:
        duration = None
    return sha256, duration


def autofill_audio_fields(folder: Path, manifest: dict, verbose: bool) -> None:
    """Fills manifest['audio']['sha256']/['durationSeconds'] from the real audio file when
    missing or a known placeholder. Raises PackError if a *present* sha256 doesn't match the
    actual file — catches a wrong/stale audio file in the folder instead of shipping a
    capsule that fails its own integrity check on open."""
    audio = manifest["audio"]
    entry = audio.get("entry", "")
    audio_path = folder / entry
    if not audio_path.exists():
        raise PackError(f"audio.entry file not found: {entry}")

    actual_sha, actual_duration = compute_audio_meta(audio_path)

    current_sha = str(audio.get("sha256", ""))
    if current_sha in PLACEHOLDER_VALUES:
        audio["sha256"] = actual_sha
        if verbose:
            print(f"  filled audio.sha256 = {actual_sha}")
    elif current_sha != actual_sha:
        raise PackError(
            f"manifest audio.sha256 ({current_sha}) does not match the actual audio file "
            f"({actual_sha}) — wrong file, or a stale manifest?"
        )

    current_duration = audio.get("durationSeconds")
    needs_duration = current_duration in (None, 0) or str(current_duration) in PLACEHOLDER_VALUES
    if needs_duration and actual_duration:
        audio["durationSeconds"] = actual_duration
        if verbose:
            print(f"  filled audio.durationSeconds = {actual_duration}")


def zip_folder(folder: Path) -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        for path in sorted(folder.rglob("*")):
            if path.is_dir():
                continue
            arcname = path.relative_to(folder).as_posix()
            z.write(path, arcname)
    return buf.getvalue()


def load_signing_key(path: Path) -> Ed25519PrivateKey:
    priv = serialization.load_pem_private_key(path.read_bytes(), password=None)
    if not isinstance(priv, Ed25519PrivateKey):
        raise PackError(f"{path} is not an Ed25519 private key")
    return priv


def sign_and_write(payload: bytes, priv: Ed25519PrivateKey, out_path: Path) -> None:
    pubkey_bytes = priv.public_key().public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)
    signature = priv.sign(payload)
    header = MAGIC + FORMAT_VERSION.to_bytes(4, "little") + pubkey_bytes + signature
    out_path.write_bytes(header + payload)


def self_verify(out_path: Path) -> None:
    data = out_path.read_bytes()
    if data[0:4] != MAGIC:
        raise PackError("self-verify failed: bad magic in written file")
    if int.from_bytes(data[4:8], "little") != FORMAT_VERSION:
        raise PackError("self-verify failed: bad format version in written file")

    pubkey_bytes = data[8:40]
    sig_bytes = data[40:104]
    payload = data[104:]

    pubkey = Ed25519PublicKey.from_public_bytes(pubkey_bytes)
    try:
        pubkey.verify(sig_bytes, payload)
    except InvalidSignature as exc:
        raise PackError("self-verify failed: signature does not verify against the written payload") from exc

    with zipfile.ZipFile(io.BytesIO(payload)) as z:
        bad = z.testzip()
        if bad is not None:
            raise PackError(f"self-verify failed: corrupt zip entry {bad!r}")
        manifest = json.loads(z.read("manifest.json"))
        validate_manifest(manifest)
        audio_bytes = z.read(manifest["audio"]["entry"])
        actual_sha = hashlib.sha256(audio_bytes).hexdigest()
        if actual_sha != manifest["audio"]["sha256"]:
            raise PackError(
                f"self-verify failed: audio sha256 mismatch ({actual_sha} vs {manifest['audio']['sha256']})"
            )
        entry_count = len(z.namelist())

    fingerprint = hashlib.sha256(pubkey_bytes).hexdigest()
    print(f"self-verify OK: fingerprint={fingerprint}, {entry_count} entries, audio sha verified, signature valid")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("folder", type=Path, help="Capsule folder containing manifest.json")
    parser.add_argument("--key", type=Path, required=True, help="Ed25519 private key .pem to sign with")
    parser.add_argument("-o", "--output", type=Path, help="Output path (default: <manifest.id>.spectralis)")
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args()

    if not args.folder.is_dir():
        print(f"error: {args.folder} is not a directory", file=sys.stderr)
        return 1

    manifest_path = args.folder / "manifest.json"
    if not manifest_path.exists():
        print(f"error: {manifest_path} not found", file=sys.stderr)
        return 1

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        print("validating manifest.json ...")
        validate_manifest(manifest)

        for warning in check_referenced_files_exist(args.folder, manifest):
            print(f"  warning: {warning}")

        print("checking audio.sha256/durationSeconds ...")
        autofill_audio_fields(args.folder, manifest, args.verbose)
        manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")

        priv = load_signing_key(args.key)

        out_path = args.output or Path(f"{manifest['id']}.spectralis")

        print(f"zipping {args.folder} ...")
        payload = zip_folder(args.folder)

        print(f"signing and writing {out_path} ...")
        sign_and_write(payload, priv, out_path)

        self_verify(out_path)
    except PackError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    except json.JSONDecodeError as exc:
        print(f"error: manifest.json is not valid JSON: {exc}", file=sys.stderr)
        return 1

    print(f"done: {out_path} ({out_path.stat().st_size:,} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
