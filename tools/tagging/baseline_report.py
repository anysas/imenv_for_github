#!/usr/bin/env python3
"""Baseline per-tag distribution from curated-props.json (no scoring, manifest unchanged)."""
from __future__ import annotations

import argparse
import json
from collections import Counter
from datetime import datetime
from pathlib import Path
from typing import Dict, List

from io_manifest import load_manifest
from taxonomy import load_taxonomy

REPO_ROOT = Path(__file__).resolve().parents[2]
MANIFEST_PATH = REPO_ROOT / "Assets/StreamingAssets/curated-props.json"
TAXONOMY_PATH = REPO_ROOT / "Assets/StreamingAssets/tag-taxonomy-v1.json"
REPORT_DIR = REPO_ROOT / "curation-reports"


def _tags_from_prop(p: Dict, key: str) -> List[str]:
    return [str(t).strip().lower() for t in (p.get(key) or []) if str(t).strip()]


def _personal_tags(p: Dict) -> List[str]:
    emo = _tags_from_prop(p, "emotional_tags")
    per = _tags_from_prop(p, "personal_tags")
    return sorted(set(emo) | set(per)) if (emo or per) else []


def _corporate_tags(p: Dict) -> List[str]:
    return _tags_from_prop(p, "corporate_tags")


def per_tag_stats(
    vocab: List[str],
    dist: Counter,
    n_models: int,
    max_slots_per_model: int,
    floor_ratio: float,
    cap_ratio: float,
) -> Dict[str, Dict]:
    """Expected uniform-ish target from N * max_slots / len(vocab); deficit vs dynamic floor/cap."""
    vn = len(vocab) or 1
    total_assignments = sum(dist.values())
    target = (n_models * max_slots_per_model) / vn if n_models else 0.0
    floor = max(1, int(floor_ratio * target)) if n_models else 0
    cap = int(cap_ratio * target + 1) if n_models else 0
    out: Dict[str, Dict] = {}
    for t in vocab:
        c = dist.get(t, 0)
        out[t] = {
            "count": c,
            "target": round(target, 2),
            "floor": floor,
            "cap": cap,
            "deficit": max(0, floor - c),
            "surplus": max(0, c - cap),
        }
    return out


def build_baseline(
    manifest_path: Path,
    taxonomy_path: Path,
    floor_ratio: float,
    cap_ratio: float,
    max_personal: int,
    max_corporate: int,
) -> Dict:
    taxonomy = load_taxonomy(taxonomy_path)
    _, props = load_manifest(manifest_path)
    personal_vocab = taxonomy["personal_tags"]
    corporate_vocab = taxonomy["corporate_tags"]

    personal_dist: Counter = Counter()
    corporate_dist: Counter = Counter()
    for p in props:
        personal_dist.update(_personal_tags(p))
        corporate_dist.update(_corporate_tags(p))

    n = len(props)
    personal_detail = per_tag_stats(personal_vocab, personal_dist, n, max_personal, floor_ratio, cap_ratio)
    corporate_detail = per_tag_stats(corporate_vocab, corporate_dist, n, max_corporate, floor_ratio, cap_ratio)

    tags_below_floor_p = [t for t, d in personal_detail.items() if d["deficit"] > 0]
    tags_above_cap_p = [t for t, d in personal_detail.items() if d["surplus"] > 0]
    tags_below_floor_c = [t for t, d in corporate_detail.items() if d["deficit"] > 0]
    tags_above_cap_c = [t for t, d in corporate_detail.items() if d["surplus"] > 0]

    return {
        "meta": {
            "source_manifest": str(manifest_path),
            "taxonomy": str(taxonomy_path),
            "generated_at": datetime.now().isoformat(timespec="seconds"),
            "n_models": n,
            "floor_ratio": floor_ratio,
            "cap_ratio": cap_ratio,
            "max_personal_slots": max_personal,
            "max_corporate_slots": max_corporate,
        },
        "summary": {
            "total_personal_assignments": int(sum(personal_dist.values())),
            "total_corporate_assignments": int(sum(corporate_dist.values())),
            "unique_personal_tags_used": len([t for t in personal_vocab if personal_dist.get(t, 0) > 0]),
            "unique_corporate_tags_used": len([t for t in corporate_vocab if corporate_dist.get(t, 0) > 0]),
            "personal_tags_below_floor": len(tags_below_floor_p),
            "personal_tags_above_cap": len(tags_above_cap_p),
            "corporate_tags_below_floor": len(tags_below_floor_c),
            "corporate_tags_above_cap": len(tags_above_cap_c),
        },
        "personal_tags": personal_detail,
        "corporate_tags": corporate_detail,
    }


def main() -> int:
    p = argparse.ArgumentParser(description="Baseline tag distribution report (read-only).")
    p.add_argument("--manifest", type=Path, default=MANIFEST_PATH)
    p.add_argument("--taxonomy", type=Path, default=TAXONOMY_PATH)
    p.add_argument("--floor-ratio", type=float, default=0.25, help="Floor = ratio * uniform target (N*max/tags)")
    p.add_argument("--cap-ratio", type=float, default=2.5, help="Cap = ratio * uniform target")
    p.add_argument("--max-personal", type=int, default=8, help="Max slots per model for target/floor math")
    p.add_argument("--max-corporate", type=int, default=4)
    p.add_argument("--out", type=Path, default=None, help="Output JSON path (default curation-reports/baseline_*.json)")
    args = p.parse_args()

    payload = build_baseline(
        args.manifest,
        args.taxonomy,
        args.floor_ratio,
        args.cap_ratio,
        args.max_personal,
        args.max_corporate,
    )

    REPORT_DIR.mkdir(parents=True, exist_ok=True)
    out = args.out or (REPORT_DIR / f"baseline_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json")
    out.write_text(json.dumps(payload, indent=2))
    print(f"[baseline] wrote {out}")
    s = payload["summary"]
    print(
        f"[baseline] models={payload['meta']['n_models']} "
        f"personal_below_floor={s['personal_tags_below_floor']} corporate_below_floor={s['corporate_tags_below_floor']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
