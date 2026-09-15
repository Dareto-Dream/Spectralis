"""Scaffolds a new Wasm/wgpu album-world crate from spectralis-world-sdk's rust-minimal
template. See spectral://docs/world-sdk-host-imports (or spectralis-world-sdk/docs/host-imports.md
directly) for the exact host-import API the scaffolded crate can call, and
spectral://docs/world-sdk-readme for what a world can/can't do today — no scene-graph, one flat
vertex/index buffer via submit_geometry, the host still owns the only camera.
"""

from __future__ import annotations

import re
import shutil
from pathlib import Path

import formats

TEMPLATE_DIR = formats.WORLD_SDK_ROOT / "templates" / "rust-minimal"


def scaffold_wasm_world(dest_folder: str, crate_name: str) -> dict:
    if not TEMPLATE_DIR.exists():
        raise FileNotFoundError(
            f"template not found at {TEMPLATE_DIR} — is spectralis-world-sdk checked out there? "
            "override with the SPECTRALIS_WORLD_SDK_PATH env var if it lives elsewhere"
        )

    dest = Path(dest_folder).expanduser().resolve()
    if dest.exists() and any(dest.iterdir()):
        raise FileExistsError(f"{dest} already exists and is not empty")

    # Skip the template's own `target/` build output — it's a stale, possibly large artifact
    # from building the template itself, not part of what a new crate should start with.
    shutil.copytree(TEMPLATE_DIR, dest, ignore=shutil.ignore_patterns("target"))

    cargo_toml = dest / "Cargo.toml"
    if cargo_toml.exists():
        text = cargo_toml.read_text(encoding="utf-8")
        text, count = re.subn(r'(?m)^name\s*=\s*".*"$', f'name = "{crate_name}"', text, count=1)
        if count:
            cargo_toml.write_text(text, encoding="utf-8")

    wasm_file_stem = crate_name.replace("-", "_")

    return {
        "dest_folder": str(dest),
        "crate_name": crate_name,
        "build_command": "cargo build --target wasm32-unknown-unknown --release",
        "output_wasm": f"target/wasm32-unknown-unknown/release/{wasm_file_stem}.wasm",
        "next_steps": [
            "edit src/lib.rs — implement on_load/on_tick/on_unload and call submit_geometry "
            "for real geometry (see spectral://docs/world-sdk-host-imports)",
            "cargo build --target wasm32-unknown-unknown --release",
            "point manifest.world.wasmEntry at the built .wasm inside your album-world folder, "
            "and add the worlds.wasm3d capability",
        ],
    }
