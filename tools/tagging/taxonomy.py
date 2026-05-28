from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, List


def load_taxonomy(path: Path) -> Dict[str, List[str]]:
    data = json.loads(path.read_text())
    personal = [str(x).strip().lower() for x in data.get("personal_tags", []) if str(x).strip()]
    corporate = [str(x).strip().lower() for x in data.get("corporate_tags", []) if str(x).strip()]

    if len(personal) < 64:
        raise ValueError(f"Expected at least 64 personal tags, found {len(personal)}")
    if len(corporate) < 16:
        raise ValueError(f"Expected at least 16 corporate tags, found {len(corporate)}")

    return {
        "personal_tags": sorted(set(personal)),
        "corporate_tags": sorted(set(corporate)),
    }
