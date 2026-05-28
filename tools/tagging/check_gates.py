#!/usr/bin/env python3
"""Check curated-props tag distribution against floor/cap gates (exit 0 = pass)."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

TAGGING_DIR = Path(__file__).resolve().parent
REPO_ROOT = TAGGING_DIR.parents[1]
if str(TAGGING_DIR) not in sys.path:
    sys.path.insert(0, str(TAGGING_DIR))

from io_manifest import load_manifest  # noqa: E402
from reporting import evaluate_tag_gates  # noqa: E402
from taxonomy import load_taxonomy  # noqa: E402

MANIFEST_PATH = REPO_ROOT / "Assets/StreamingAssets/curated-props.json"
TAXONOMY_PATH = REPO_ROOT / "Assets/StreamingAssets/tag-taxonomy-v1.json"
REPORT_DIR = REPO_ROOT / "curation-reports"


def main() -> int:
    p = argparse.ArgumentParser(description="Verify tag floor/cap gates on curated-props.json")
    p.add_argument("--manifest", type=Path, default=MANIFEST_PATH)
    p.add_argument("--taxonomy", type=Path, default=TAXONOMY_PATH)
    p.add_argument("--floor-ratio", type=float, default=0.25)
    p.add_argument("--cap-ratio", type=float, default=2.5)
    p.add_argument("--max-personal", type=int, default=8)
    p.add_argument("--max-corporate", type=int, default=4)
    p.add_argument("--ignore-cap", action="store_true", help="Only enforce floor (ignore tags above cap)")
    p.add_argument("--json-out", type=Path, default=None, help="Write gate evaluation JSON")
    args = p.parse_args()

    taxonomy = load_taxonomy(args.taxonomy)
    _, props = load_manifest(args.manifest)
    ok, block = evaluate_tag_gates(
        props,
        taxonomy,
        args.floor_ratio,
        args.cap_ratio,
        args.max_personal,
        args.max_corporate,
        enforce_cap=not args.ignore_cap,
    )

    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(json.dumps(block, indent=2))

    print(
        f"[gates] models={len(props)} personal_below_floor={len(block['personal']['tags_below_floor'])} "
        f"corporate_below_floor={len(block['corporate']['tags_below_floor'])} "
        f"personal_above_cap={len(block['personal']['tags_above_cap'])} "
        f"corporate_above_cap={len(block['corporate']['tags_above_cap'])} ok={ok}"
    )
    if not ok:
        if block["personal"]["tags_below_floor"]:
            print("[gates] personal below floor:", ", ".join(block["personal"]["tags_below_floor"][:40]))
        if block["corporate"]["tags_below_floor"]:
            print("[gates] corporate below floor:", ", ".join(block["corporate"]["tags_below_floor"]))
        if not args.ignore_cap:
            if block["personal"]["tags_above_cap"]:
                print("[gates] personal above cap:", ", ".join(block["personal"]["tags_above_cap"][:40]))
            if block["corporate"]["tags_above_cap"]:
                print("[gates] corporate above cap:", ", ".join(block["corporate"]["tags_above_cap"]))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
