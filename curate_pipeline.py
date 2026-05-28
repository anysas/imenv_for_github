#!/usr/bin/env python3
"""
curate_pipeline.py — GLB validation, dimension extraction, manifest enrichment.

Pass 1 — Validate every GLB in the manifest:
  - Magic bytes "glTF", parseable JSON chunk, non-zero geometry.
Pass 2 — Extract real AABB from POSITION accessor min/max.
Pass 3 — Size categorisation, confidence scoring, per-group natural-range check.
Pass 4 — Remove invalid props; write enriched manifest + human-readable report.

Run from project root:
  python3 curate_pipeline.py [--dry-run] [--workers N]

Outputs (unless --dry-run):
  Assets/StreamingAssets/curated-props.json   (replaced in-place)
  curate_report.json                           (summary + removed-prop list)
"""

import json
import os
import re
import struct
import sys
import argparse
import math
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed
from collections import Counter

# ─────────────────────────────────────────────────────────────────────────────
# Paths
# ─────────────────────────────────────────────────────────────────────────────
MODELS_ROOT   = Path("Assets/StreamingAssets/models")
MANIFEST_PATH = Path("Assets/StreamingAssets/curated-props.json")
OVERLAY_PATH  = Path("Assets/StreamingAssets/curation_overrides.json")
REPORT_PATH   = Path("curate_report.json")

# ─────────────────────────────────────────────────────────────────────────────
# Size categories (longest axis in metres)
# ─────────────────────────────────────────────────────────────────────────────
SIZE_BUCKETS = [
    (0.000, 0.030, "tiny"),       # < 3 cm — coins, nuts/bolts, usually unplaceable
    (0.030, 0.150, "small"),      # 3–15 cm — mugs, small tools, trinkets
    (0.150, 0.500, "medium"),     # 15–50 cm — books, boxes, appliances
    (0.500, 1.800, "large"),      # 50 cm – 1.8 m — chairs, cabinets, doors
    (1.800, 6.000, "oversized"),  # 1.8–6 m — vehicles, large architecture
    (6.000, 9e9,   "huge"),       # > 6 m — almost always wrong-scale level geometry
]

# Per-group natural size ranges (longest axis, metres).
# Models outside range get a confidence penalty.
GROUP_NATURAL_RANGES = {
    "item":      (0.04,  2.0),
    "lab":       (0.05,  2.5),
    "furniture": (0.20,  3.0),
    "retail":    (0.05,  4.0),
    "office":    (0.05,  2.5),
    "workshop":  (0.05,  4.0),
    "domestic":  (0.05,  3.0),
    "tech":      (0.03,  1.5),
}

# Props with confidence < this threshold are removed from the manifest.
CONFIDENCE_THRESHOLD = 0.30

# Hard-remove size categories regardless of confidence.
HARD_REMOVE_SIZES = {"huge"}

# ─────────────────────────────────────────────────────────────────────────────
# GLB parsing helpers
# ─────────────────────────────────────────────────────────────────────────────

def _read_glb_json(path: Path):
    """
    Open a GLB file and return the parsed JSON chunk as a dict.
    Raises ValueError on any structural problem.
    """
    with open(path, "rb") as fh:
        raw = fh.read()

    if len(raw) < 20:
        raise ValueError("File too small to be a valid GLB")

    magic, version, total_len = struct.unpack_from("<4sII", raw, 0)
    if magic != b"glTF":
        raise ValueError(f"Bad magic: {magic!r}")
    if version not in (1, 2):
        raise ValueError(f"Unsupported GLB version: {version}")
    if total_len > len(raw):
        raise ValueError("GLB claims larger size than file on disk (truncated)")

    chunk0_len, chunk0_type = struct.unpack_from("<I4s", raw, 12)
    if chunk0_type != b"JSON":
        raise ValueError(f"First chunk is not JSON: {chunk0_type!r}")

    json_bytes = raw[20:20 + chunk0_len]
    try:
        return json.loads(json_bytes)
    except json.JSONDecodeError as e:
        raise ValueError(f"JSON chunk parse error: {e}") from e


def _extract_aabb(gltf: dict):
    """
    Extract a global AABB from all POSITION accessor min/max in the glTF JSON.
    Returns (dx, dy, dz) in metres, or (0, 0, 0) if no positional data found.
    Vertex count (sum of POSITION accessor counts) is also returned.
    """
    accessors = gltf.get("accessors", [])
    meshes    = gltf.get("meshes", [])

    # Build set of accessor indices used as POSITION attributes
    position_indices = set()
    for mesh in meshes:
        for prim in mesh.get("primitives", []):
            pos_idx = prim.get("attributes", {}).get("POSITION")
            if pos_idx is not None:
                position_indices.add(pos_idx)

    if not position_indices:
        return 0.0, 0.0, 0.0, 0

    global_min = [math.inf,  math.inf,  math.inf]
    global_max = [-math.inf, -math.inf, -math.inf]
    total_vertices = 0

    for idx in position_indices:
        if idx >= len(accessors):
            continue
        acc = accessors[idx]
        amin = acc.get("min")
        amax = acc.get("max")
        count = acc.get("count", 0)
        total_vertices += count

        if amin and amax and len(amin) == 3 and len(amax) == 3:
            for i in range(3):
                global_min[i] = min(global_min[i], amin[i])
                global_max[i] = max(global_max[i], amax[i])

    if math.isinf(global_min[0]):
        return 0.0, 0.0, 0.0, total_vertices

    dx = abs(global_max[0] - global_min[0])
    dy = abs(global_max[1] - global_min[1])
    dz = abs(global_max[2] - global_min[2])
    return dx, dy, dz, total_vertices


# ─────────────────────────────────────────────────────────────────────────────
# Per-prop validation
# ─────────────────────────────────────────────────────────────────────────────

def _size_category(longest_axis: float) -> str:
    for lo, hi, label in SIZE_BUCKETS:
        if lo <= longest_axis < hi:
            return label
    return "huge"


def _score_confidence(prop: dict, dx: float, dy: float, dz: float,
                      vertex_count: int, has_valid_dims: bool) -> float:
    confidence = 1.0
    longest = max(dx, dy, dz)

    if not has_valid_dims:
        confidence -= 0.30   # dimensions unknown — hard to validate
    else:
        lo, hi = GROUP_NATURAL_RANGES.get(prop.get("group", "item"), (0.04, 4.0))
        if longest < lo * 0.4:
            confidence -= 0.30   # absurdly small (likely unit-scale error)
        elif longest > hi * 3.0:
            confidence -= 0.35   # absurdly large (level geometry, wrong scale)
        elif lo <= longest <= hi:
            confidence += 0.10   # natural fit for this group

    poly = prop.get("poly_count", 0)
    if vertex_count > 0:
        if vertex_count < 12:
            confidence -= 0.20   # degenerate mesh
        elif vertex_count > 4:
            confidence += 0.05   # non-trivial geometry (small bonus)

    # poly_count was estimated during manifest build; low count is suspicious
    if poly < 6:
        confidence -= 0.15

    return round(max(0.0, min(1.0, confidence)), 3)


def validate_prop(prop: dict, models_root: Path) -> dict:
    """
    Validates and enriches a single prop entry.
    Returns a result dict:
      {
        "id": ...,
        "keep": True|False,
        "reason": "ok"|"missing_file"|"invalid_glb"|"zero_geometry"|"hard_remove_size"|...,
        "dx": float, "dy": float, "dz": float,
        "vertex_count": int,
        "confidence": float,
        "size_category": str,
      }
    """
    glb_path = models_root / prop.get("glb_path", "")
    result = {
        "id": prop.get("id"),
        "keep": False,
        "reason": "unknown",
        "dx": 0.0, "dy": 0.0, "dz": 0.0,
        "vertex_count": 0,
        "confidence": 0.0,
        "size_category": "unknown",
    }

    # ── 1. File existence ────────────────────────────────────────────────────
    if not glb_path.exists():
        result["reason"] = "missing_file"
        return result

    # ── 2. GLB structural validation ────────────────────────────────────────
    try:
        gltf = _read_glb_json(glb_path)
    except (ValueError, OSError, struct.error) as e:
        result["reason"] = f"invalid_glb:{e}"
        return result

    # ── 3. Geometry extraction ───────────────────────────────────────────────
    dx, dy, dz, vertex_count = _extract_aabb(gltf)
    result["dx"] = round(dx, 4)
    result["dy"] = round(dy, 4)
    result["dz"] = round(dz, 4)
    result["vertex_count"] = vertex_count
    has_valid_dims = (dx + dy + dz) > 0.001

    if vertex_count < 3:
        result["reason"] = "zero_geometry"
        return result

    # ── 4. Size categorisation ───────────────────────────────────────────────
    longest = max(dx, dy, dz) if has_valid_dims else 0.0
    size_cat = _size_category(longest)
    result["size_category"] = size_cat

    if size_cat in HARD_REMOVE_SIZES:
        result["reason"] = "hard_remove_size"
        result["confidence"] = 0.0
        return result

    # ── 5. Confidence score ──────────────────────────────────────────────────
    confidence = _score_confidence(prop, dx, dy, dz, vertex_count, has_valid_dims)
    result["confidence"] = confidence

    if confidence < CONFIDENCE_THRESHOLD:
        result["reason"] = f"low_confidence:{confidence}"
        return result

    result["keep"] = True
    result["reason"] = "ok"
    return result


# ─────────────────────────────────────────────────────────────────────────────
# Curation overlay loader
# ─────────────────────────────────────────────────────────────────────────────

def _load_overlay() -> dict:
    """
    Load curation_overrides.json produced by CurationLab.
    Returns a dict with keys: removed_ids (set), overrides (dict id→entry).
    Always returns a valid structure even if the file is absent or corrupt.
    """
    empty = {"removed_ids": set(), "overrides": {}, "custom_groups": []}
    if not OVERLAY_PATH.exists():
        return empty
    try:
        with open(OVERLAY_PATH) as f:
            data = json.load(f)
        return {
            "removed_ids":   set(data.get("removed_ids", [])),
            "overrides":     data.get("overrides", {}),
            "custom_groups": data.get("custom_groups", []),
        }
    except Exception as e:
        print(f"[curate_pipeline] WARNING: could not load overlay ({e}); ignoring.")
        return empty


def _apply_overlay_entry(prop: dict, entry: dict) -> None:
    """Mutate a prop dict in-place from a CurationLab overlay entry."""
    if entry.get("group"):
        prop["group"] = entry["group"]
    if entry.get("emotional_tags") is not None:
        prop["emotional_tags"] = entry["emotional_tags"]
    if entry.get("scale_override", 0) > 0.001:
        prop["scale_override"] = round(entry["scale_override"], 5)
    if entry.get("custom_tags"):
        prop["custom_tags"] = entry["custom_tags"]
    if entry.get("notes"):
        prop["notes"] = entry["notes"]


# ─────────────────────────────────────────────────────────────────────────────
# Main pipeline
# ─────────────────────────────────────────────────────────────────────────────

def run(dry_run: bool = False, workers: int = 8):
    print(f"[curate_pipeline] Loading manifest from {MANIFEST_PATH} …")
    with open(MANIFEST_PATH) as f:
        data = json.load(f)
    props = data.get("props", [])
    print(f"[curate_pipeline] {len(props)} props to validate (workers={workers})")

    overlay = _load_overlay()
    if overlay["removed_ids"] or overlay["overrides"]:
        print(f"[curate_pipeline] Overlay: "
              f"{len(overlay['removed_ids'])} manual removals, "
              f"{len(overlay['overrides'])} overrides")

    results_by_id = {}
    completed = 0

    with ThreadPoolExecutor(max_workers=workers) as pool:
        futures = {pool.submit(validate_prop, p, MODELS_ROOT): p for p in props}
        for fut in as_completed(futures):
            res = fut.result()
            results_by_id[res["id"]] = res
            completed += 1
            if completed % 200 == 0:
                kept_so_far = sum(1 for r in results_by_id.values() if r["keep"])
                print(f"  … {completed}/{len(props)} done  ({kept_so_far} kept so far)")

    # ── Build enriched props list ────────────────────────────────────────────
    kept_props = []
    removed_props = []
    overlay_applied = 0

    for prop in props:
        pid = prop["id"]

        # Manual removal from CurationLab overlay takes precedence over pipeline decision.
        if pid in overlay["removed_ids"]:
            removed_props.append({
                "id": pid,
                "glb_path": prop.get("glb_path"),
                "reason": "manual_removal",
            })
            continue

        res = results_by_id.get(pid)
        if res is None or not res["keep"]:
            removed_props.append({
                "id": pid,
                "glb_path": prop.get("glb_path"),
                "reason": res["reason"] if res else "no_result",
            })
            continue

        # Enrich dimensions from parsed GLB (override zeros)
        prop["dimensions"] = {
            "x": res["dx"],
            "y": res["dy"],
            "z": res["dz"],
        }
        prop["size_category"] = res["size_category"]
        prop["confidence"]    = res["confidence"]
        prop["vertex_count"]  = res["vertex_count"]

        # Apply CurationLab overrides (group, emotional_tags, scale_override, …)
        if pid in overlay["overrides"]:
            _apply_overlay_entry(prop, overlay["overrides"][pid])
            overlay_applied += 1

        kept_props.append(prop)

    if overlay_applied:
        print(f"[curate_pipeline] Applied {overlay_applied} overlay overrides to kept props.")

    # ── Statistics ───────────────────────────────────────────────────────────
    remove_reasons = Counter(r["reason"] for r in removed_props)
    size_dist      = Counter(p["size_category"] for p in kept_props)
    group_dist     = Counter(p["group"] for p in kept_props)
    conf_dist      = Counter(
        "high" if p["confidence"] >= 0.8 else
        "mid"  if p["confidence"] >= 0.5 else "low"
        for p in kept_props
    )

    print(f"\n[curate_pipeline] ── Results ──────────────────────────────────────")
    print(f"  Input:   {len(props):>5} props")
    print(f"  Kept:    {len(kept_props):>5} props  ({100*len(kept_props)/len(props):.1f}%)")
    print(f"  Removed: {len(removed_props):>5} props")
    print(f"\n  Removal reasons:")
    for reason, count in remove_reasons.most_common():
        print(f"    {reason:<40} {count:>5}")
    print(f"\n  Size distribution (kept):")
    for size, count in size_dist.most_common():
        print(f"    {size:<15} {count:>5}")
    print(f"\n  Group distribution (kept):")
    for grp, count in group_dist.most_common():
        print(f"    {grp:<15} {count:>5}")
    print(f"\n  Confidence (kept): high={conf_dist['high']}  mid={conf_dist['mid']}  low={conf_dist['low']}")

    # ── Report ───────────────────────────────────────────────────────────────
    report = {
        "input_count":   len(props),
        "kept_count":    len(kept_props),
        "removed_count": len(removed_props),
        "removal_reasons": dict(remove_reasons),
        "size_distribution": dict(size_dist),
        "group_distribution": dict(group_dist),
        "confidence_distribution": dict(conf_dist),
        "removed": removed_props,
    }

    if dry_run:
        print(f"\n[curate_pipeline] DRY RUN — no files written.")
        with open(REPORT_PATH, "w") as f:
            json.dump(report, f, indent=2)
        print(f"[curate_pipeline] Report written to {REPORT_PATH}")
        return

    # ── Write enriched manifest ──────────────────────────────────────────────
    data["props"] = kept_props
    with open(MANIFEST_PATH, "w") as f:
        json.dump(data, f, separators=(",", ":"))
    print(f"\n[curate_pipeline] Wrote {len(kept_props)} props to {MANIFEST_PATH}")

    with open(REPORT_PATH, "w") as f:
        json.dump(report, f, indent=2)
    print(f"[curate_pipeline] Report written to {REPORT_PATH}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Curate and enrich the prop manifest.")
    parser.add_argument("--dry-run",  action="store_true", help="Analyse without writing files")
    parser.add_argument("--workers",  type=int, default=8, help="Thread pool size (default 8)")
    args = parser.parse_args()
    run(dry_run=args.dry_run, workers=args.workers)
