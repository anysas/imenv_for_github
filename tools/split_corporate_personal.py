#!/usr/bin/env python3
"""
Split curated-props.game-ready.json into personal vs corporate gameplay pools.

Personal groups (player hotbar): domestic, furniture, item, workshop
Corporate groups (system placement): office, lab, tech, retail

Writes:
  Assets/StreamingAssets/curated-props.personal.json
  Assets/StreamingAssets/curated-props.corporate.json

Optional report:
  curation-reports/corporate_personal_split_<stamp>.json
"""
from __future__ import annotations

import argparse
import json
from collections import Counter
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Set

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = REPO_ROOT / "Assets/StreamingAssets/curated-props.game-ready.json"
REPORT_DIR = REPO_ROOT / "curation-reports"

PERSONAL_GROUPS = frozenset({"domestic", "furniture", "item", "workshop"})
CORPORATE_GROUPS = frozenset({"office", "lab", "tech", "retail"})

# Optional manual overrides (prop id)
FORCE_CORPORATE: Set[str] = set()
FORCE_PERSONAL: Set[str] = set()


def _norm_id(x: Any) -> str:
    return str(x).strip() if x is not None else ""


def _norm_group(x: Any) -> str:
    return str(x).strip().lower() if x is not None else ""


def classify_prop(prop: Dict[str, Any]) -> str:
    """Return 'personal', 'corporate', or 'unknown'."""
    pid = _norm_id(prop.get("id"))
    if pid in FORCE_CORPORATE:
        return "corporate"
    if pid in FORCE_PERSONAL:
        return "personal"

    group = _norm_group(prop.get("group"))
    if group in CORPORATE_GROUPS:
        return "corporate"
    if group in PERSONAL_GROUPS:
        return "personal"
    return "unknown"


def split_corporate_personal(
    source_path: Path,
    out_personal: Path,
    out_corporate: Path,
) -> Dict[str, Any]:
    data = json.loads(source_path.read_text())
    props: List[Dict[str, Any]] = data.get("props") or []
    if not isinstance(props, list):
        raise ValueError("Expected top-level 'props' array")

    personal: List[Dict[str, Any]] = []
    corporate: List[Dict[str, Any]] = []
    unknown: List[Dict[str, Any]] = []
    group_counts = Counter()

    for p in props:
        group_counts[_norm_group(p.get("group"))] += 1
        bucket = classify_prop(p)
        if bucket == "personal":
            personal.append(p)
        elif bucket == "corporate":
            corporate.append(p)
        else:
            unknown.append(p)

    def write_out(path: Path, subset: List[Dict[str, Any]]) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"props": subset}, indent=2))

    write_out(out_personal, personal)
    write_out(out_corporate, corporate)

    return {
        "generated_at": datetime.now().isoformat(timespec="seconds"),
        "source": str(source_path),
        "counts": {
            "source_total": len(props),
            "personal": len(personal),
            "corporate": len(corporate),
            "unknown": len(unknown),
        },
        "groups_in_source": dict(group_counts),
        "personal_groups": sorted(PERSONAL_GROUPS),
        "corporate_groups": sorted(CORPORATE_GROUPS),
        "unknown_sample_ids": [_norm_id(p.get("id")) for p in unknown[:30]],
        "outputs": {
            "personal": str(out_personal),
            "corporate": str(out_corporate),
        },
    }


def main() -> int:
    p = argparse.ArgumentParser(description="Split game-ready manifest into personal vs corporate pools.")
    p.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    p.add_argument(
        "--out-personal",
        type=Path,
        default=REPO_ROOT / "Assets/StreamingAssets/curated-props.personal.json",
    )
    p.add_argument(
        "--out-corporate",
        type=Path,
        default=REPO_ROOT / "Assets/StreamingAssets/curated-props.corporate.json",
    )
    p.add_argument("--report", type=Path, default=None)
    args = p.parse_args()

    if not args.source.is_file():
        print(f"[split] ERROR: source not found: {args.source}")
        return 1

    summary = split_corporate_personal(args.source, args.out_personal, args.out_corporate)

    REPORT_DIR.mkdir(parents=True, exist_ok=True)
    report_path = args.report or (
        REPORT_DIR / f"corporate_personal_split_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json"
    )
    report_path.write_text(json.dumps(summary, indent=2))

    c = summary["counts"]
    print(
        f"[split] source={c['source_total']} personal={c['personal']} corporate={c['corporate']} "
        f"unknown={c['unknown']}"
    )
    print(f"[split] -> {args.out_personal.name} / {args.out_corporate.name}")
    if c["unknown"]:
        print(f"[split] WARN: {c['unknown']} props with unclassified group (not written to either file)")
    print(f"[split] report: {report_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
