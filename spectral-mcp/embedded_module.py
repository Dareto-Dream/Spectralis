"""ID3v2 DELTA_MODULE_/DELTA_DATA_/DELTA_BIN_ embedding — docs/formats/metadata-embedding.md.

Written fresh from that spec (no prior Python example embeds these specific frames —
metadata/*/tag_*.py scripts only write standard ID3 tags like TIT2/APIC/USLT, a different
mechanism from this one), using mutagen the same way those scripts do.

Execution-status note (see docs/blockers.md, "Blocker 3"): the current app's
EmbeddedModuleReader reads these frames, but there is no in-process WASM executor yet — an
embedded `type: "visualizer"`/`runtime: "wasm"` module will round-trip through this tool fine
but won't currently render in the app. `type: "html"` modules do have a working host
(docs/feature-gap.md). Markdown/video execution status isn't confirmed here — check
docs/feature-gap.md before relying on them for something time-sensitive.
"""

from __future__ import annotations

import base64
import json
from pathlib import Path

from mutagen.id3 import ID3, ID3NoHeaderError, TXXX

import formats

MODULE_PREFIX = "DELTA_MODULE_"
DATA_PREFIX = "DELTA_DATA_"
BIN_PREFIX = "DELTA_BIN_"


def _validate_module(module: dict) -> None:
    for field in ("id", "type", "runtime", "binaryRef"):
        if not module.get(field):
            raise ValueError(f"module.{field} is required")
    if len(module["id"]) > formats.MODULE_ID_MAX_LEN:
        raise ValueError(f"module.id exceeds {formats.MODULE_ID_MAX_LEN} chars")
    if module["type"].lower() not in formats.MODULE_TYPES:
        raise ValueError(f"module.type must be one of {sorted(formats.MODULE_TYPES)}, got {module['type']!r}")
    if module["runtime"].lower() not in formats.MODULE_RUNTIMES:
        raise ValueError(
            f"module.runtime must be one of {sorted(formats.MODULE_RUNTIMES)}, got {module['runtime']!r}"
        )


def embed_module(
    mp3_path: str,
    module: dict,
    binary_path: str,
    data_blocks: dict[str, str] | None = None,
) -> dict:
    """Embeds one module descriptor + its binary asset + optional data blocks into mp3_path's
    ID3 tags, validating docs/formats/metadata-embedding.md's size limits before writing
    anything (fails clean rather than writing something the app will silently skip later).

    `module` must include at least id/type/runtime/binaryRef (binaryRef names the DELTA_BIN_
    frame this call writes — the binary embedded is read from `binary_path`, not looked up by
    that id). `data_blocks` maps binding name -> text/JSON string, written as DELTA_DATA_
    frames matching module.dataRefs keys (not cross-checked here)."""
    _validate_module(module)

    path = Path(mp3_path).expanduser().resolve()
    if not path.exists():
        raise FileNotFoundError(str(path))

    bin_path = Path(binary_path).expanduser().resolve()
    if not bin_path.exists():
        raise FileNotFoundError(str(bin_path))
    binary_bytes = bin_path.read_bytes()

    is_video = module["type"].lower() == "video"
    max_binary = formats.VIDEO_BINARY_MAX if is_video else formats.BINARY_ASSET_MAX
    if len(binary_bytes) > max_binary:
        raise ValueError(
            f"binary asset is {len(binary_bytes):,} bytes, exceeds the "
            f"{'video' if is_video else 'non-video'} limit of {max_binary:,} bytes"
        )

    for name, value in (data_blocks or {}).items():
        size = len(value.encode("utf-8"))
        if size > formats.DATA_BLOCK_MAX:
            raise ValueError(f"data block {name!r} is {size:,} bytes, exceeds {formats.DATA_BLOCK_MAX:,}")

    try:
        tags = ID3(path)
    except ID3NoHeaderError:
        tags = ID3()

    module_desc = f"{MODULE_PREFIX}{module['id']}"
    tags.delall(f"TXXX:{module_desc}")
    tags.add(TXXX(encoding=3, desc=module_desc, text=json.dumps(module)))

    binary_ref = module["binaryRef"]
    bin_desc = f"{BIN_PREFIX}{binary_ref}"
    tags.delall(f"TXXX:{bin_desc}")
    tags.add(TXXX(encoding=3, desc=bin_desc, text=base64.b64encode(binary_bytes).decode("ascii")))

    written_data_frames = []
    for name, value in (data_blocks or {}).items():
        data_desc = f"{DATA_PREFIX}{name}"
        tags.delall(f"TXXX:{data_desc}")
        tags.add(TXXX(encoding=3, desc=data_desc, text=value))
        written_data_frames.append(data_desc)

    tags.save(path, v2_version=3)

    return {
        "path": str(path),
        "module_frame": module_desc,
        "binary_frame": bin_desc,
        "binary_bytes": len(binary_bytes),
        "data_frames": written_data_frames,
    }


def read_modules(mp3_path: str) -> list[dict]:
    """Reads back every DELTA_MODULE_* frame's JSON from mp3_path — no binaries/data blocks,
    just the module descriptors, for confirming a round-trip or inspecting what's embedded."""
    path = Path(mp3_path).expanduser().resolve()
    if not path.exists():
        raise FileNotFoundError(str(path))
    try:
        tags = ID3(path)
    except ID3NoHeaderError:
        return []

    modules = []
    for frame in tags.getall("TXXX"):
        if not frame.desc.startswith(MODULE_PREFIX):
            continue
        try:
            modules.append(json.loads(str(frame.text[0])))
        except (json.JSONDecodeError, IndexError):
            continue
    return modules
