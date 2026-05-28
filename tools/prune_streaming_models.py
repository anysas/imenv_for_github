#!/usr/bin/env python3
"""
Safely prune unreferenced GLB files from Assets/StreamingAssets/models.

Default behavior:
- Uses curated-props.game-ready.json as the keep-list source.
- Dry-run only unless --apply is provided.
- Moves unreferenced files (and .meta sidecars) to an external archive path.

This removes project/bundle bloat while preserving rollback ability.
"""
from __future__ import annotations

import argparse
import json
import shutil
from dataclasses import asdict, dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, Iterable, List, Set


REPO_ROOT = Path(__file__).resolve().parents[1]
STREAMING = REPO_ROOT / "Assets/StreamingAssets"
MODELS_DIR = STREAMING / "models"
DEFAULT_MANIFEST = STREAMING / "curated-props.game-ready.json"
FALLBACK_MANIFEST = STREAMING / "curated-props.json"
SEED_SESSIONS_DIR = STREAMING / "seed_sessions"
REPORT_DIR = REPO_ROOT / "curation-reports"


def _norm_glb_rel(path_str: str) -> str:
    s = (path_str or "").strip().replace("\\", "/")
    while s.startswith("./"):
        s = s[2:]
    if s.startswith("models/"):
        s = s[len("models/") :]
    return s.strip("/")


def _load_manifest_references(manifest_path: Path) -> Set[str]:
    data = json.loads(manifest_path.read_text())
    props = data.get("props") or []
    if not isinstance(props, list):
        raise ValueError(f"Invalid manifest format (missing list props): {manifest_path}")
    refs: Set[str] = set()
    for p in props:
        rel = _norm_glb_rel(str((p or {}).get("glb_path") or ""))
        if rel:
            refs.add(rel)
    return refs


def _load_session_references(session_json_paths: Iterable[Path]) -> Set[str]:
    refs: Set[str] = set()
    for path in session_json_paths:
        try:
            data = json.loads(path.read_text())
        except Exception:
            continue
        if not isinstance(data, dict):
            continue
        placements = data.get("Placements") or data.get("placements") or []
        if not isinstance(placements, list):
            continue
        for row in placements:
            if not isinstance(row, dict):
                continue
            rel = _norm_glb_rel(str(row.get("GlbPath") or row.get("glb_path") or ""))
            if rel:
                refs.add(rel)
    return refs


def _session_json_files(session_dirs: Iterable[Path]) -> List[Path]:
    out: List[Path] = []
    for d in session_dirs:
        if not d.is_dir():
            continue
        out.extend(sorted(p for p in d.glob("*.json") if p.is_file()))
    return out


def _default_persistent_session_dir() -> Path:
    return Path.home() / "Library/Application Support/yptox + Alyssa Diaz/Evil Minecraft/sessions"


def _bytes_human(num: int) -> str:
    n = float(num)
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if n < 1024.0:
            return f"{n:.2f} {unit}"
        n /= 1024.0
    return f"{n:.2f} PB"


@dataclass
class FileMove:
    src: str
    dst: str
    bytes: int
    type: str


def _list_existing_glb_rel(models_dir: Path) -> Set[str]:
    rels: Set[str] = set()
    for p in models_dir.rglob("*.glb"):
        rels.add(p.relative_to(models_dir).as_posix())
    return rels


def _prune_empty_dirs(root: Path) -> int:
    removed = 0
    for d in sorted((p for p in root.rglob("*") if p.is_dir()), key=lambda p: len(p.parts), reverse=True):
        try:
            next(d.iterdir())
        except StopIteration:
            d.rmdir()
            removed += 1
        except Exception:
            continue
    return removed


def _write_report(report_path: Path, payload: Dict[str, Any]) -> None:
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(payload, indent=2))


def _collect_file_records(models_dir: Path, rels: Iterable[str], include_meta: bool) -> List[Path]:
    out: List[Path] = []
    for rel in rels:
        glb = models_dir / rel
        if glb.is_file():
            out.append(glb)
        if include_meta:
            meta = glb.with_suffix(glb.suffix + ".meta")
            if meta.is_file():
                out.append(meta)
    return out


def run(args: argparse.Namespace) -> int:
    manifest_path = args.manifest
    models_dir = args.models_dir
    if not manifest_path.is_file():
        raise FileNotFoundError(f"Manifest not found: {manifest_path}")
    if not models_dir.is_dir():
        raise FileNotFoundError(f"Models directory not found: {models_dir}")

    refs = _load_manifest_references(manifest_path)
    manifest_refs_count = len(refs)
    extra_manifest_refs = 0
    for extra in args.extra_manifest:
        if not extra.is_file():
            continue
        extra_refs = _load_manifest_references(extra)
        extra_manifest_refs += len(extra_refs)
        refs |= extra_refs
    session_dirs: List[Path] = []
    if not args.no_seed_sessions:
        session_dirs.append(SEED_SESSIONS_DIR)
    if args.session_dir:
        session_dirs.extend(args.session_dir)
    auto_live = _default_persistent_session_dir()
    if args.include_live_sessions and auto_live.is_dir():
        session_dirs.append(auto_live)

    session_refs = _load_session_references(_session_json_files(session_dirs))
    refs |= session_refs
    existing_glb = _list_existing_glb_rel(models_dir)

    missing_from_disk = sorted(r for r in refs if not (models_dir / r).is_file())
    unreferenced = sorted(existing_glb - refs)
    kept = sorted(existing_glb & refs)

    keep_bytes = sum((models_dir / r).stat().st_size for r in kept if (models_dir / r).is_file())
    prune_bytes = sum((models_dir / r).stat().st_size for r in unreferenced if (models_dir / r).is_file())

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    archive_root = args.archive_root
    if archive_root is None:
        archive_root = REPO_ROOT.parent / "_asset_archives" / f"{REPO_ROOT.name}_prune_{stamp}"
    archive_root = archive_root.resolve()

    report_path = args.report or (REPORT_DIR / f"model_prune_{stamp}.json")

    applied = False
    actions: List[FileMove] = []
    removed_dirs = 0

    if args.apply and unreferenced:
        files_to_process = _collect_file_records(models_dir, unreferenced, include_meta=(not args.skip_meta))
        if args.hard_delete:
            for src in files_to_process:
                size = src.stat().st_size if src.is_file() else 0
                src.unlink(missing_ok=True)
                actions.append(FileMove(src=str(src), dst="", bytes=size, type="delete"))
        else:
            for src in files_to_process:
                rel = src.relative_to(models_dir)
                dst = archive_root / "models" / rel
                dst.parent.mkdir(parents=True, exist_ok=True)
                size = src.stat().st_size if src.is_file() else 0
                shutil.move(str(src), str(dst))
                actions.append(FileMove(src=str(src), dst=str(dst), bytes=size, type="move"))
        removed_dirs = _prune_empty_dirs(models_dir)
        applied = True

    payload: Dict[str, Any] = {
        "generated_at": datetime.now().isoformat(timespec="seconds"),
        "mode": {
            "apply": bool(args.apply),
            "hard_delete": bool(args.hard_delete),
            "skip_meta": bool(args.skip_meta),
        },
        "inputs": {
            "manifest": str(manifest_path),
            "models_dir": str(models_dir),
            "archive_root": str(archive_root),
            "session_dirs": [str(x) for x in session_dirs],
        },
        "counts": {
            "referenced_in_manifest": manifest_refs_count,
            "referenced_in_extra_manifests": extra_manifest_refs,
            "referenced_from_sessions": len(session_refs),
            "total_referenced_keep_set": len(refs),
            "existing_glb_on_disk": len(existing_glb),
            "kept_glb": len(kept),
            "unreferenced_glb": len(unreferenced),
            "missing_from_disk": len(missing_from_disk),
            "file_actions": len(actions),
            "empty_dirs_removed": removed_dirs,
        },
        "sizes": {
            "kept_glb_bytes": keep_bytes,
            "kept_glb_human": _bytes_human(keep_bytes),
            "unreferenced_glb_bytes": prune_bytes,
            "unreferenced_glb_human": _bytes_human(prune_bytes),
        },
        "samples": {
            "missing_from_disk": missing_from_disk[:100],
            "unreferenced_glb": unreferenced[:200],
        },
        "applied": applied,
        "actions": [asdict(a) for a in actions[:2000]],
        "action_truncated": len(actions) > 2000,
    }

    _write_report(report_path, payload)

    print(
        f"[prune] manifest_refs={manifest_refs_count} extra_manifest_refs={extra_manifest_refs} "
        f"session_refs={len(session_refs)} "
        f"total_keep={len(refs)} existing_glb={len(existing_glb)} "
        f"unreferenced_glb={len(unreferenced)} reclaimable={_bytes_human(prune_bytes)}"
    )
    if missing_from_disk:
        print(f"[prune] WARN: missing referenced files on disk: {len(missing_from_disk)}")
    if args.apply:
        mode = "hard-delete" if args.hard_delete else "archive-move"
        print(f"[prune] APPLY {mode}: actions={len(actions)} empty_dirs_removed={removed_dirs}")
    else:
        print("[prune] DRY RUN: no files changed. Re-run with --apply to execute.")
    print(f"[prune] report: {report_path}")
    if args.apply and not args.hard_delete:
        print(f"[prune] archive: {archive_root}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Prune unreferenced models from StreamingAssets/models.")
    p.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST, help="Manifest used as keep list")
    p.add_argument(
        "--extra-manifest",
        type=Path,
        action="append",
        default=[FALLBACK_MANIFEST],
        help="Additional manifest(s) whose glb_path refs are always preserved (repeatable)",
    )
    p.add_argument("--models-dir", type=Path, default=MODELS_DIR, help="GLB root directory")
    p.add_argument(
        "--archive-root",
        type=Path,
        default=None,
        help="Archive destination when applying non-hard-delete mode",
    )
    p.add_argument(
        "--session-dir",
        type=Path,
        action="append",
        default=[],
        help="Additional directory containing session JSON files to preserve referenced GLBs",
    )
    p.add_argument(
        "--no-seed-sessions",
        action="store_true",
        help="Do not include StreamingAssets/seed_sessions JSON refs in keep-set",
    )
    p.add_argument(
        "--include-live-sessions",
        action="store_true",
        help="Include refs from ~/Library/Application Support/.../Evil Minecraft/sessions if present",
    )
    p.add_argument("--report", type=Path, default=None, help="JSON report output path")
    p.add_argument("--skip-meta", action="store_true", help="Do not move/delete .meta sidecars")
    p.add_argument("--apply", action="store_true", help="Execute move/delete actions")
    p.add_argument("--hard-delete", action="store_true", help="Permanently delete instead of archive-move")
    return p


if __name__ == "__main__":
    parser = build_parser()
    cli = parser.parse_args()
    if cli.hard_delete and not cli.apply:
        parser.error("--hard-delete requires --apply")
    raise SystemExit(run(cli))
