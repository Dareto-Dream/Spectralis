"""Ed25519 signing-key discovery/generation under ~/.spectralis/keys/ — the same convention
every existing pack_*.py / world_to_lite.py script already uses. Never returns private key
bytes from a tool result; only fingerprints (public, safe to share) and file names.
"""

from __future__ import annotations

import hashlib
from dataclasses import dataclass
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

import formats


@dataclass
class KeyInfo:
    file_name: str
    key_id: str
    fingerprint: str


def load_key(path: Path) -> Ed25519PrivateKey:
    priv = serialization.load_pem_private_key(path.read_bytes(), password=None)
    if not isinstance(priv, Ed25519PrivateKey):
        raise ValueError(f"{path} is not an Ed25519 private key")
    return priv


def identity_of(priv: Ed25519PrivateKey) -> tuple[str, bytes]:
    """Returns (fingerprint_hex, raw_public_key_bytes)."""
    pub_bytes = priv.public_key().public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)
    fingerprint = hashlib.sha256(pub_bytes).hexdigest()
    return fingerprint, pub_bytes


def list_keys() -> list[KeyInfo]:
    if not formats.KEYS_DIR.exists():
        return []
    out: list[KeyInfo] = []
    for path in sorted(formats.KEYS_DIR.glob("*.pem")):
        try:
            priv = load_key(path)
        except Exception:
            continue
        fingerprint, _pub = identity_of(priv)
        out.append(KeyInfo(file_name=path.name, key_id=path.stem, fingerprint=fingerprint))
    return out


def resolve_key(key_path: str | None) -> Path:
    """Resolves an explicit key path, or auto-selects when exactly one local key exists."""
    if key_path:
        p = Path(key_path).expanduser()
        if not p.exists():
            raise FileNotFoundError(f"signing key not found: {p}")
        return p
    keys = list_keys()
    if not keys:
        raise FileNotFoundError(
            f"no signing keys found under {formats.KEYS_DIR} — call generate_signing_key first, "
            "or pass key_path explicitly"
        )
    if len(keys) > 1:
        names = ", ".join(k.file_name for k in keys)
        raise ValueError(f"multiple signing keys found ({names}) — pass key_path to disambiguate")
    return formats.KEYS_DIR / keys[0].file_name


def generate_key(key_id: str) -> KeyInfo:
    """Mints a brand-new local Ed25519 identity. This is local-only — getting it trusted by
    real Spectralis installs still requires registering the public key with the CDN's creator
    key infrastructure (docs/cdn-contract.md) by hand; this function cannot do that part.
    Refuses to overwrite an existing key file."""
    if not key_id or any(c in key_id for c in '\\/:*?"<>|') or key_id in (".", ".."):
        raise ValueError(f"invalid key_id: {key_id!r}")

    formats.KEYS_DIR.mkdir(parents=True, exist_ok=True)
    out_path = formats.KEYS_DIR / f"{key_id}.pem"
    if out_path.exists():
        raise FileExistsError(f"a key already exists at {out_path} — refusing to overwrite")

    priv = Ed25519PrivateKey.generate()
    pem = priv.private_bytes(
        serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()
    )
    out_path.write_bytes(pem)

    fingerprint, _pub = identity_of(priv)
    return KeyInfo(file_name=out_path.name, key_id=key_id, fingerprint=fingerprint)
