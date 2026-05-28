from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, List, Tuple


def load_manifest(path: Path) -> Tuple[Dict, List[Dict]]:
    data = json.loads(path.read_text())
    props = data.get("props", [])
    if not isinstance(props, list):
        raise ValueError("Manifest props is not a list")
    return data, props


def save_manifest(path: Path, root: Dict, props: List[Dict]) -> None:
    root["props"] = sorted(
        props,
        key=lambda p: ((p.get("id") or ""), (p.get("display_name") or "")),
    )
    path.write_text(json.dumps(root, indent=2))
