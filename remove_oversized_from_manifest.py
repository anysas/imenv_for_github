#!/usr/bin/env python3
"""
remove_oversized_from_manifest.py

Removes any prop entries with size_category == "oversized" from
Assets/StreamingAssets/curated-props.json.

Run from project root:
  python3 remove_oversized_from_manifest.py
  python3 curate_pipeline.py
"""

import json
from pathlib import Path

MANIFEST_PATH = Path("Assets/StreamingAssets/curated-props.json")


def main() -> int:
    if not MANIFEST_PATH.exists():
        raise FileNotFoundError(MANIFEST_PATH)

    with MANIFEST_PATH.open() as f:
        data = json.load(f)

    props = list(data.get("props", []))
    before = len(props)
    kept = [p for p in props if p.get("size_category") != "oversized"]
    removed = before - len(kept)

    # keep groups field consistent with remaining props
    groups = sorted({p.get("group") for p in kept if p.get("group")})

    data["props"] = kept
    data["total"] = len(kept)
    data["groups"] = groups

    with MANIFEST_PATH.open("w") as f:
        json.dump(data, f, separators=(",", ":"))

    print(f"[remove_oversized] removed={removed} kept={len(kept)} path={MANIFEST_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

