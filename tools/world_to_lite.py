#!/usr/bin/env python3
"""
world_to_lite.py — derive a Spectral World "Lite" bundle from a full .spectral album world.

A Lite bundle is the auto-generated export described in the roadmap's Phase 3: it strips the
sandboxed Wasm/wgpu world payload (manifest.world.wasmEntry) and the worlds.wasm3d capability,
keeping everything else byte-for-byte — tracks, audio, art, lyrics, the HTML world/story
content — untouched. Any DSP preset the retained HTML content registers at runtime
(spectral.dsp.register()) keeps working unchanged, since that's a capability
(audio.dspPreset) and a runtime call, not something declared in world/, so there's nothing to
strip for it.

The result is re-signed with the SAME creator key as the source package (must be available
locally — this tool derives a variant, it doesn't mint a new creator identity), so it validates
through the exact same CDN-key/trust flow as the original. See docs/formats/spectral-album-world.md
for the full binary/manifest format this script reads and writes.

Usage:
    python world_to_lite.py <input.spectral> [-o output.spectral] [--key path/to/key.pem] [-v]
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

MAGIC = b"SPAC"
FORMAT_VERSION = 1
HEADER_LEN = 104  # 4 magic + 4 version + 32 pubkey + 64 signature
WASM_CAPABILITY = "worlds.wasm3d"


class PackageError(ValueError):
    """A problem with the source or derived .spectral package itself (not a usage error)."""


def read_spac(path: Path) -> tuple[bytes, bytes, bytes]:
    """Validates the SPAC header and signature, returning (pubkey, signature, zip_payload)."""
    data = path.read_bytes()
    if len(data) < HEADER_LEN:
        raise PackageError(f"{path}: too short ({len(data)} bytes) to be a .spectral file")

    magic = data[0:4]
    if magic != MAGIC:
        raise PackageError(f"{path}: bad magic {magic!r}, expected {MAGIC!r} ('SPAC')")

    version = int.from_bytes(data[4:8], "little")
    if version != FORMAT_VERSION:
        raise PackageError(f"{path}: unsupported format version {version}, expected {FORMAT_VERSION}")

    pubkey_bytes = data[8:40]
    sig_bytes = data[40:104]
    payload = data[104:]

    pubkey = Ed25519PublicKey.from_public_bytes(pubkey_bytes)
    try:
        pubkey.verify(sig_bytes, payload)
    except InvalidSignature as exc:
        raise PackageError(f"{path}: signature verification failed - refusing to touch an unsigned/corrupt package") from exc

    return pubkey_bytes, sig_bytes, payload


def find_candidate_keys() -> list[Path]:
    """Mirrors the per-song pack_<song>.py convention: look under ~/.spectralis/keys/."""
    keys_dir = Path.home() / ".spectralis" / "keys"
    if not keys_dir.exists():
        return []
    return sorted(keys_dir.glob("*.pem"))


def load_private_key_matching(pubkey_bytes: bytes, candidates: list[Path]) -> Ed25519PrivateKey:
    for candidate_path in candidates:
        try:
            priv = serialization.load_pem_private_key(candidate_path.read_bytes(), password=None)
        except Exception:
            continue
        if not isinstance(priv, Ed25519PrivateKey):
            continue
        candidate_pub = priv.public_key().public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)
        if candidate_pub == pubkey_bytes:
            return priv

    fingerprint = hashlib.sha256(pubkey_bytes).hexdigest()
    raise PackageError(
        f"no local key matches the source package's signing key (fingerprint {fingerprint}) - "
        "a Lite bundle is re-signed as the same creator, so that private key must be available "
        "locally (default search: ~/.spectralis/keys/*.pem), or pass --key explicitly"
    )


def strip_wasm_world(payload: bytes, verbose: bool) -> bytes:
    """Returns a new zip payload with world.wasmEntry and its file removed from the manifest,
    and the worlds.wasm3d capability dropped. Raises PackageError if there's nothing to strip
    (already a Lite bundle, or no world section at all)."""
    src = zipfile.ZipFile(io.BytesIO(payload), "r")
    manifest = json.loads(src.read("manifest.json"))

    world = manifest.get("world")
    wasm_entry = (world or {}).get("wasmEntry", "")
    if not wasm_entry:
        raise PackageError("source package declares no world.wasmEntry - nothing to strip (already a Lite bundle?)")

    if verbose:
        print(f"  stripping wasm entry: {wasm_entry}")

    world.pop("wasmEntry", None)
    capabilities = manifest.get("capabilities", [])
    stripped_caps = [c for c in capabilities if c != WASM_CAPABILITY]
    if verbose and len(stripped_caps) != len(capabilities):
        print(f"  dropped capability: {WASM_CAPABILITY}")
    manifest["capabilities"] = stripped_caps

    out_buf = io.BytesIO()
    with zipfile.ZipFile(out_buf, "w", zipfile.ZIP_DEFLATED) as dst:
        for item in src.infolist():
            if item.filename in ("manifest.json", wasm_entry):
                continue
            dst.writestr(item, src.read(item.filename))
        dst.writestr("manifest.json", json.dumps(manifest, indent=2))

    return out_buf.getvalue()


def sign_and_write(payload: bytes, priv: Ed25519PrivateKey, out_path: Path) -> None:
    pubkey_bytes = priv.public_key().public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)
    signature = priv.sign(payload)
    header = MAGIC + FORMAT_VERSION.to_bytes(4, "little") + pubkey_bytes + signature
    out_path.write_bytes(header + payload)


def self_verify(out_path: Path) -> None:
    """Re-reads the file we just wrote through the exact same validation path a real player
    would use, and confirms the strip actually took — mirrors pack_<song>.py's self-verify step."""
    pubkey_bytes, _sig, payload = read_spac(out_path)  # raises on any signature/format problem

    with zipfile.ZipFile(io.BytesIO(payload)) as z:
        bad_entry = z.testzip()
        if bad_entry is not None:
            raise PackageError(f"self-verify failed: corrupt zip entry {bad_entry!r}")
        manifest = json.loads(z.read("manifest.json"))
        entry_count = len(z.namelist())

    if manifest.get("world", {}).get("wasmEntry"):
        raise PackageError("self-verify failed: wasmEntry still present in output manifest")
    if WASM_CAPABILITY in manifest.get("capabilities", []):
        raise PackageError(f"self-verify failed: {WASM_CAPABILITY} still declared in output capabilities")

    fingerprint = hashlib.sha256(pubkey_bytes).hexdigest()
    print(f"self-verify OK: fingerprint={fingerprint}, {entry_count} entries, no wasm payload, signature valid")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("input", type=Path, help="Path to the source .spectral file")
    parser.add_argument("-o", "--output", type=Path, help="Output path (default: <input>-lite.spectral)")
    parser.add_argument("--key", type=Path, help="Explicit signing key .pem (skips ~/.spectralis/keys/ auto-discovery)")
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args()

    if not args.input.exists():
        print(f"error: {args.input} not found", file=sys.stderr)
        return 1

    out_path = args.output or args.input.with_name(args.input.stem + "-lite" + args.input.suffix)

    try:
        print(f"reading {args.input} ...")
        pubkey_bytes, _sig, payload = read_spac(args.input)
        print(f"  signature OK, fingerprint={hashlib.sha256(pubkey_bytes).hexdigest()}")

        if args.key:
            priv = serialization.load_pem_private_key(args.key.read_bytes(), password=None)
            if not isinstance(priv, Ed25519PrivateKey):
                raise PackageError(f"--key {args.key} is not an Ed25519 private key")
            candidate_pub = priv.public_key().public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)
            if candidate_pub != pubkey_bytes:
                raise PackageError(f"--key {args.key} does not match the source package's signing key")
        else:
            priv = load_private_key_matching(pubkey_bytes, find_candidate_keys())

        print("stripping wasm world payload ...")
        lite_payload = strip_wasm_world(payload, args.verbose)

        print(f"signing and writing {out_path} ...")
        sign_and_write(lite_payload, priv, out_path)

        self_verify(out_path)
    except PackageError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    print(f"done: {out_path} ({out_path.stat().st_size:,} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
