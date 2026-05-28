#!/usr/bin/env python3
"""
Split curated-props.json into game-ready vs non-ready manifests using curation_overrides.json.

Game-ready: id in reviewed_ids AND id not in removed_ids (and present in base manifest).
Non-ready: every other prop row in the base manifest.

Writes:
  Assets/StreamingAssets/curated-props.game-ready.json
  Assets/StreamingAssets/curated-props.non-ready.json
Optional report:
  curation-reports/game_ready_split_<stamp>.json
"""
from __future__ import annotations

import argparse
import json
from collections import Counter
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Set, Tuple

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_MANIFEST = REPO_ROOT / "Assets/StreamingAssets/curated-props.json"
DEFAULT_OVERLAY = REPO_ROOT / "Assets/StreamingAssets/curation_overrides.json"
MODELS_DIR = REPO_ROOT / "Assets/StreamingAssets/models"
REPORT_DIR = REPO_ROOT / "curation-reports"


def _load_json(path: Path) -> Dict[str, Any]:
    return json.loads(path.read_text())


def _norm_id(x: Any) -> str:
    return str(x).strip() if x is not None else ""


def split_manifests(
    manifest_path: Path,
    overlay_path: Path,
    out_game_ready: Path,
    out_non_ready: Path,
    check_glb: bool,
) -> Dict[str, Any]:
    base = _load_json(manifest_path)
    props: List[Dict[str, Any]] = base.get("props") or []
    if not isinstance(props, list):
        raise ValueError("curated-props.json: expected top-level 'props' array")

    overlay: Dict[str, Any] = {}
    if overlay_path.is_file():
        overlay = _load_json(overlay_path)

    reviewed_raw = overlay.get("reviewed_ids") or []
    removed_raw = overlay.get("removed_ids") or []

    reviewed: Set[str] = {_norm_id(x) for x in reviewed_raw if _norm_id(x)}
    removed: Set[str] = {_norm_id(x) for x in removed_raw if _norm_id(x)}

    id_counts = Counter(_norm_id(p.get("id")) for p in props)
    dupes = sorted([i for i, c in id_counts.items() if c > 1 and i])

    by_id: Dict[str, Dict[str, Any]] = {}
    for p in props:
        pid = _norm_id(p.get("id"))
        if not pid:
            continue
        if pid in by_id:
            continue
        by_id[pid] = p

    game_ready: List[Dict[str, Any]] = []
    non_ready: List[Dict[str, Any]] = []

    missing_glb: List[str] = []
    reviewed_not_in_manifest: List[str] = sorted(reviewed - set(by_id.keys()))

    for p in props:
        pid = _norm_id(p.get("id"))
        if not pid:
            non_ready.append(p)
            continue

        is_ready = pid in reviewed and pid not in removed
        if is_ready:
            game_ready.append(p)
        else:
            non_ready.append(p)

        if check_glb and is_ready:
            rel = str(p.get("glb_path") or "").strip().replace("\\", "/")
            if not rel:
                missing_glb.append(pid)
                continue
            # manifest paths are usually like "foo/bar.glb" under models/
            candidate = MODELS_DIR / rel
            if not candidate.is_file():
                # try if rel already includes models/
                alt = REPO_ROOT / "Assets/StreamingAssets" / rel
                if not alt.is_file():
                    missing_glb.append(f"{pid} -> {rel}")

    def write_out(path: Path, subset: List[Dict[str, Any]]) -> None:
        out = {"props": subset}
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(out, indent=2))

    write_out(out_game_ready, game_ready)
    write_out(out_non_ready, non_ready)

    summary = {
        "generated_at": datetime.now().isoformat(timespec="seconds"),
        "source_manifest": str(manifest_path),
        "overlay": str(overlay_path) if overlay_path.is_file() else None,
        "counts": {
            "base_props": len(props),
            "game_ready": len(game_ready),
            "non_ready": len(non_ready),
            "reviewed_ids": len(reviewed),
            "removed_ids": len(removed),
        },
        "integrity": {
            "duplicate_ids_in_base": dupes[:50],
            "duplicate_id_count": len(dupes),
            "reviewed_not_in_manifest_sample": reviewed_not_in_manifest[:50],
            "reviewed_not_in_manifest_count": len(reviewed_not_in_manifest),
            "game_ready_missing_glb_sample": missing_glb[:80],
            "game_ready_missing_glb_count": len(missing_glb),
        },
        "outputs": {
            "game_ready": str(out_game_ready),
            "non_ready": str(out_non_ready),
        },
    }
    return summary


def main() -> int:
    p = argparse.ArgumentParser(description="Split curated manifest into game-ready vs non-ready.")
    p.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    p.add_argument("--overlay", type=Path, default=DEFAULT_OVERLAY)
    p.add_argument(
        "--out-game-ready",
        type=Path,
        default=REPO_ROOT / "Assets/StreamingAssets/curated-props.game-ready.json",
    )
    p.add_argument(
        "--out-non-ready",
        type=Path,
        default=REPO_ROOT / "Assets/StreamingAssets/curated-props.non-ready.json",
    )
    p.add_argument("--no-glb-check", action="store_true", help="Skip GLB existence validation")
    p.add_argument("--report", type=Path, default=None, help="Write JSON summary report")
    args = p.parse_args()

    summary = split_manifests(
        args.manifest,
        args.overlay,
        args.out_game_ready,
        args.out_non_ready,
        check_glb=not args.no_glb_check,
    )

    REPORT_DIR.mkdir(parents=True, exist_ok=True)
    report_path = args.report or (
        REPORT_DIR / f"game_ready_split_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json"
    )
    report_path.write_text(json.dumps(summary, indent=2))

    c = summary["counts"]
    print(
        f"[split] base={c['base_props']} game_ready={c['game_ready']} non_ready={c['non_ready']} "
        f"-> {args.out_game_ready.name} / {args.out_non_ready.name}"
    )
    ig = summary["integrity"]
    if ig.get("game_ready_missing_glb_count"):
        print(f"[split] WARN: {ig['game_ready_missing_glb_count']} game-ready rows missing GLB on disk")
    if ig.get("duplicate_id_count"):
        print(f"[split] WARN: {ig['duplicate_id_count']} duplicate ids in base manifest")
    print(f"[split] report: {report_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
