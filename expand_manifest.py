#!/usr/bin/env python3
"""
expand_manifest.py — bulk-ingest additional GLBs into curated-props.json.

This script intentionally adds *stub* prop entries (id/path/display_name/group/emotional_tags, etc.)
and relies on curate_pipeline.py to:
  - validate GLB structure + geometry
  - measure real dimensions from POSITION accessor AABB
  - compute size_category / confidence / vertex_count
  - remove invalid/bad-scale assets

Run from project root:
  python3 expand_manifest.py --max-add 2000
  python3 curate_pipeline.py
"""

from __future__ import annotations

import argparse
import json
import os
import re
import math
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Sequence, Set, Tuple


MODELS_ROOT = Path("Assets/StreamingAssets/models")
MANIFEST_PATH = Path("Assets/StreamingAssets/curated-props.json")

OVERSIZED_AXIS_M = 1.8  # keep < 1.8m (pipeline labels >=1.8 as oversized)


# -----------------------------------------------------------------------------
# Skip rules (folder + filename heuristics)
# -----------------------------------------------------------------------------

SKIP_PATH_PARTS: Tuple[str, ...] = (
    "gibs/",
    "deadbodies/",
    "infected/",
    "foliage/",
    "player/",
    "humans/",
    "npcs/",
    "props_vehicles/",
    "props_foliage/",
    "props_rocks/",
    "props_debris/",
    "props_junk/",
    "props_wasteland/",
    "props_combine/",
    "props_c17/",
    "props_silo/",
    "props_pipes/",
    "props_windows/",
    "props_doors/",
    "props_highway/",
    "props_buildables/",
    "props_bts/",
    "props_destruction/",
    "a4_destruction/",
    "props_map_editor/",
    "props_underground/",
    "props_exteriors/",
    "lighthouse/",
    "lostcoast/",
    "props_mill/",
)

SKIP_FNAME_SUFFIXES: Tuple[str, ...] = (
    "_reference",
    "_lod1",
    "_lod2",
    "_lod3",
    "_skybox",
)

SKIP_FNAME_CONTAINS: Tuple[str, ...] = (
    "vortigaunt",
    "stalker",
    "scanner",
    "strider",
    "hunter",
    "roller",
    "zombie",
    "survivor",
    "witch",
    "smoker",
    "boomer",
    "jockey",
    "charger",
    "spitter",
)

_GIB_RE = re.compile(r"(?:_gib\\d+|_chunk\\d+)$", re.IGNORECASE)
_DAMAGE_RE = re.compile(r"(?:_damage(?:_\\d+)?|_dam\\d+[a-z]?)$", re.IGNORECASE)


def should_skip(rel_path: str) -> bool:
    p = rel_path.replace("\\\\", "/").lower()
    fname = p.split("/")[-1]
    stem = fname[:-4] if fname.endswith(".glb") else fname

    if any(part in p for part in SKIP_PATH_PARTS):
        return True

    if "skybox" in stem:
        return True

    if _GIB_RE.search(stem) is not None:
        return True
    if _DAMAGE_RE.search(stem) is not None:
        return True

    if any(stem.endswith(suf) for suf in SKIP_FNAME_SUFFIXES):
        return True

    if any(tok in stem for tok in SKIP_FNAME_CONTAINS):
        return True

    return False


# -----------------------------------------------------------------------------
# Lightweight AABB extraction (copied conceptually from curate_pipeline.py)
# -----------------------------------------------------------------------------

def _read_glb_json(path: Path) -> dict:
    with open(path, "rb") as fh:
        raw = fh.read()

    if len(raw) < 20:
        raise ValueError("File too small")

    magic, version, total_len = struct.unpack_from("<4sII", raw, 0)
    if magic != b"glTF":
        raise ValueError("Bad magic")
    if version not in (1, 2):
        raise ValueError("Unsupported GLB version")
    if total_len > len(raw):
        raise ValueError("Truncated GLB")

    chunk0_len, chunk0_type = struct.unpack_from("<I4s", raw, 12)
    if chunk0_type != b"JSON":
        raise ValueError("First chunk not JSON")

    json_bytes = raw[20 : 20 + chunk0_len]
    return json.loads(json_bytes)


def _extract_longest_axis(gltf: dict) -> float:
    """
    Extract a global longest axis length from POSITION accessor min/max.
    Returns 0 if no positional data found.
    """
    accessors = gltf.get("accessors", [])
    meshes = gltf.get("meshes", [])

    position_indices = set()
    for mesh in meshes:
        for prim in mesh.get("primitives", []):
            pos_idx = prim.get("attributes", {}).get("POSITION")
            if pos_idx is not None:
                position_indices.add(pos_idx)

    if not position_indices:
        return 0.0

    global_min = [math.inf, math.inf, math.inf]
    global_max = [-math.inf, -math.inf, -math.inf]

    for idx in position_indices:
        if idx >= len(accessors):
            continue
        acc = accessors[idx]
        amin = acc.get("min")
        amax = acc.get("max")
        if amin and amax and len(amin) == 3 and len(amax) == 3:
            for i in range(3):
                global_min[i] = min(global_min[i], amin[i])
                global_max[i] = max(global_max[i], amax[i])

    if math.isinf(global_min[0]):
        return 0.0

    dx = abs(global_max[0] - global_min[0])
    dy = abs(global_max[1] - global_min[1])
    dz = abs(global_max[2] - global_min[2])
    return float(max(dx, dy, dz))


def is_oversized_glb(full_path: Path, max_longest_axis_m: float) -> bool:
    """
    Returns True if the model's longest axis is >= threshold.
    If the model can't be measured, treat it as oversized (skip) to be safe.
    """
    try:
        gltf = _read_glb_json(full_path)
        longest = _extract_longest_axis(gltf)
        if longest <= 0.001:
            return True
        return longest >= max_longest_axis_m
    except Exception:
        return True


# -----------------------------------------------------------------------------
# Folder → metadata defaults
# -----------------------------------------------------------------------------

@dataclass(frozen=True)
class FolderRule:
    group: str
    emotional_tags: Tuple[str, ...]


FOLDER_RULES: List[Tuple[str, FolderRule]] = [
    ("dod/props_misc/", FolderRule("domestic", ("comforting", "domestic", "nostalgic"))),
    ("dod/props_furniture/", FolderRule("furniture", ("domestic", "nostalgic", "mundane"))),
    ("tf2/props_hearth/", FolderRule("domestic", ("comforting", "intimate", "nostalgic"))),
    ("tf2/props_manor/", FolderRule("domestic", ("comforting", "intimate", "nostalgic"))),
    ("tf2/props_medieval/", FolderRule("workshop", ("nostalgic", "personal", "mundane"))),
    ("tf2/props_frontline/", FolderRule("item", ("abandoned", "mundane", "nostalgic"))),
    ("tf2/props_embargo/", FolderRule("furniture", ("public", "mundane", "personal"))),
    ("tf2/props_farm/", FolderRule("workshop", ("nostalgic", "domestic", "mundane"))),
    ("tf2/props_mining/", FolderRule("workshop", ("mundane", "abandoned", "nostalgic"))),
    ("tf2/props_coalmines/", FolderRule("workshop", ("mundane", "abandoned", "nostalgic"))),
    ("tf2/props_halloween/", FolderRule("item", ("melancholy", "intimate", "nostalgic"))),
    ("tf2/props_mall/", FolderRule("retail", ("public", "mundane", "liminal"))),
    ("l4d2/props_mall/", FolderRule("retail", ("public", "mundane", "liminal"))),
    ("tf2/props_soho/", FolderRule("retail", ("public", "mundane", "nostalgic"))),
    ("tf2/props_tuscany/", FolderRule("retail", ("public", "mundane", "nostalgic"))),
    ("tf2/props_2fort/", FolderRule("workshop", ("mundane", "institutional", "abandoned"))),
    ("tf2/props_mvm/", FolderRule("workshop", ("mundane", "institutional", "abandoned"))),
    ("l4d2/props_fairgrounds/", FolderRule("item", ("melancholy", "liminal", "nostalgic"))),
    ("l4d2/props_urban/", FolderRule("retail", ("public", "mundane", "abandoned"))),
    ("l4d2/props_street/", FolderRule("retail", ("public", "mundane", "abandoned"))),
    ("l4d2/props_unique/", FolderRule("workshop", ("abandoned", "institutional", "mundane"))),
    ("l4d2/props_industrial/", FolderRule("workshop", ("abandoned", "institutional", "mundane"))),
    ("portal2/props_office/", FolderRule("office", ("institutional", "mundane", "personal"))),
    ("portal2/props/", FolderRule("lab", ("clinical", "institutional", "liminal"))),
    ("portal/props/", FolderRule("lab", ("clinical", "institutional", "liminal"))),
    ("css/props/", FolderRule("item", ("mundane", "public", "institutional"))),
]


def infer_rule(rel_path: str) -> FolderRule:
    p = rel_path.replace("\\\\", "/").lower()
    for prefix, rule in FOLDER_RULES:
        if p.startswith(prefix):
            return rule
    return FolderRule("item", ("mundane", "personal"))


# -----------------------------------------------------------------------------
# Naming helpers
# -----------------------------------------------------------------------------

_TRAILING_VARIANTS_RE = re.compile(r"(?:\\b(?:lod|ref)\\b\\d*|(?:\\d{2,3})|[a-z])$", re.IGNORECASE)


def make_id(rel_path: str) -> str:
    s = rel_path.replace("\\\\", "/").lower()
    s = s[:-4] if s.endswith(".glb") else s
    s = re.sub(r"[^a-z0-9]+", "_", s).strip("_")
    return s


def make_display_name(rel_path: str) -> str:
    fname = rel_path.replace("\\\\", "/").split("/")[-1]
    stem = fname[:-4] if fname.lower().endswith(".glb") else fname
    stem = stem.replace("_", " ").replace("-", " ").strip()
    stem = re.sub(r"\\s+", " ", stem)

    tokens = stem.split(" ")
    while tokens and _TRAILING_VARIANTS_RE.match(tokens[-1]):
        tokens.pop()
    if not tokens:
        tokens = [stem]

    text = " ".join(tokens).strip() or stem
    return text.title()


def infer_category(rel_path: str) -> str:
    parts = rel_path.replace("\\\\", "/").split("/")
    return parts[1] if len(parts) >= 2 else "misc"


# -----------------------------------------------------------------------------
# Manifest update
# -----------------------------------------------------------------------------

def load_manifest() -> dict:
    if not MANIFEST_PATH.exists():
        raise FileNotFoundError(f"Manifest not found: {MANIFEST_PATH}")
    with open(MANIFEST_PATH) as f:
        return json.load(f)


def save_manifest(data: dict) -> None:
    with open(MANIFEST_PATH, "w") as f:
        json.dump(data, f, separators=(",", ":"))


def iter_all_glbs(models_root: Path) -> Iterable[str]:
    for root, _, files in os.walk(models_root):
        for fn in files:
            if not fn.lower().endswith(".glb"):
                continue
            full = Path(root) / fn
            yield str(full.relative_to(models_root)).replace("\\\\", "/")


def build_existing_sets(props: Sequence[dict]) -> Tuple[Set[str], Set[str]]:
    paths: Set[str] = set()
    ids: Set[str] = set()
    for p in props:
        gp = p.get("glb_path")
        pid = p.get("id")
        if gp:
            paths.add(str(gp).replace("\\\\", "/"))
        if pid:
            ids.add(str(pid))
    return paths, ids


def make_stub_entry(rel_path: str) -> dict:
    rule = infer_rule(rel_path)
    pid = make_id(rel_path)
    return {
        "id": pid,
        "glb_path": rel_path,
        "display_name": make_display_name(rel_path),
        "group": rule.group,
        "category": infer_category(rel_path),
        "tags": [],
        "poly_count": 0,
        "dimensions": {"x": 0.0, "y": 0.0, "z": 0.0},
        "emotional_tags": list(rule.emotional_tags),
        "size_category": "unknown",
        "confidence": 1.0,
        "vertex_count": 0,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description="Bulk-add candidate GLBs to curated-props.json (stubs only).")
    ap.add_argument("--max-add", type=int, default=2000, help="Maximum number of new entries to add (default 2000)")
    ap.add_argument(
        "--max-longest-axis",
        type=float,
        default=OVERSIZED_AXIS_M,
        help="Skip models whose measured longest axis is >= this many metres (default 1.8; avoids oversized props).",
    )
    ap.add_argument("--dry-run", action="store_true", help="Do not write; just print counts")
    args = ap.parse_args()

    data = load_manifest()
    props: List[dict] = list(data.get("props", []))
    existing_paths, existing_ids = build_existing_sets(props)

    added = 0
    skipped_existing = 0
    skipped_rules = 0
    skipped_missing = 0
    skipped_oversized = 0
    groups: Set[str] = set(data.get("groups", []))

    for rel in iter_all_glbs(MODELS_ROOT):
        if rel in existing_paths:
            skipped_existing += 1
            continue
        if should_skip(rel):
            skipped_rules += 1
            continue
        full_path = MODELS_ROOT / rel
        if not full_path.exists():
            skipped_missing += 1
            continue
        if is_oversized_glb(full_path, args.max_longest_axis):
            skipped_oversized += 1
            continue

        entry = make_stub_entry(rel)
        if entry["id"] in existing_ids:
            # extremely unlikely unless paths collide after normalization
            skipped_rules += 1
            continue

        props.append(entry)
        existing_paths.add(rel)
        existing_ids.add(entry["id"])
        groups.add(entry["group"])
        added += 1
        if added >= max(0, args.max_add):
            break

    print("[expand_manifest] Existing props:", len(data.get("props", [])))
    print("[expand_manifest] Added:", added)
    print("[expand_manifest] Skipped existing:", skipped_existing)
    print("[expand_manifest] Skipped by rules:", skipped_rules)
    print("[expand_manifest] Skipped missing:", skipped_missing)
    print("[expand_manifest] Skipped oversized:", skipped_oversized)

    if args.dry_run:
        print("[expand_manifest] DRY RUN — no files written.")
        return 0

    data["props"] = props
    data["total"] = len(props)
    data["groups"] = sorted(groups)
    save_manifest(data)
    print("[expand_manifest] Wrote manifest:", MANIFEST_PATH)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""
Manifest expansion script for Algorithmic Gallery V2.

Scans high-value model directories, generates manifest entries with emotional tags,
and merges them into the existing curated-props.json.

Run from the project root:
  python3 expand_manifest.py [--dry-run] [--output curated-props.json]
"""

import json
import os
import re
import sys
from pathlib import Path

MODELS_ROOT = Path("Assets/StreamingAssets/models")
MANIFEST_PATH = Path("Assets/StreamingAssets/curated-props.json")

# ---------------------------------------------------------------------------
# 1. Target directories: (glb_dir, group, base_emotional_tags)
# ---------------------------------------------------------------------------
TARGET_DIRS = [
    # ── ORIGINAL (Pass 1) ───────────────────────────────────────────────────
    # Intimate / domestic interiors
    ("l4d2/props_interiors",   "domestic",   ["intimate", "mundane"]),
    ("hl2/props_interiors",    "domestic",   ["intimate", "nostalgic"]),
    ("l4d2/props_furniture",   "furniture",  ["comforting", "domestic"]),
    ("l4d2/props_collectables","item",       ["nostalgic", "personal"]),
    ("dod/props_furniture",    "furniture",  ["comforting", "domestic"]),
    ("tf2/props_hearth",       "domestic",   ["comforting", "nostalgic"]),
    ("tf2/props_medical",      "lab",        ["clinical", "threatening"]),
    ("tf2/props_graveyard",    "item",       ["melancholy", "nostalgic"]),
    ("tf2/props_appliances",   "domestic",   ["mundane", "domestic"]),
    ("l4d2/props_junk",        "workshop",   ["abandoned", "mundane"]),
    ("l4d2/props_downtown",    "retail",     ["public", "mundane"]),
    ("l4d2/props_office",      "office",     ["mundane", "institutional"]),
    ("l4d2/props_lab",         "lab",        ["clinical", "institutional"]),

    # ── PASS 2 ADDITIONS — new vocabulary & broader coverage ────────────────

    # LIMINAL — doors, hallways, transitional architecture
    ("l4d2/props_doors",       "item",       ["liminal", "mundane"]),
    ("dod/props_doors",        "item",       ["liminal", "nostalgic"]),

    # SACRED / ritual / memorial
    ("l4d2/props_cemetery",    "item",       ["sacred", "melancholy"]),

    # BUREAUCRATIC — offices, paperwork, admin tools
    ("portal2/props_office",   "office",     ["bureaucratic", "institutional"]),
    ("tf2/props_office",       "office",     ["bureaucratic", "mundane"]),

    # DECAYED / abandoned — wasteland, rotted environments
    ("hl2/props_canal",        "workshop",   ["decayed", "abandoned", "melancholy"]),
    ("hl2/props_wasteland",    "workshop",   ["decayed", "abandoned"]),
    ("portal2/props_underground","workshop", ["decayed", "abandoned", "threatening"]),
    ("css/props_canal",        "workshop",   ["decayed", "abandoned", "melancholy"]),

    # Threatening / institutional / dystopian (fills the thin "threatening" register)
    ("hl2/props_combine",      "lab",        ["threatening", "institutional", "clinical"]),
    ("css/props_combine",      "lab",        ["threatening", "institutional"]),
    ("portal2/props_basement", "workshop",   ["threatening", "abandoned", "liminal"]),

    # Clinical / lab — broaden beyond TF2 medical
    ("portal2/props_lab",      "lab",        ["clinical", "institutional", "bureaucratic"]),
    ("portal/props_lab",       "lab",        ["clinical", "institutional"]),
    ("hl2/props_lab",          "lab",        ["clinical", "institutional"]),
    ("css/props_lab",          "lab",        ["clinical", "institutional"]),
    ("portal2/props_factory",  "workshop",   ["institutional", "mundane"]),

    # Narrative-loaded one-offs
    ("portal2/props_diorama",  "item",       ["personal", "nostalgic"]),
    ("portal2/props_unique",   "item",       ["personal", "nostalgic"]),
    ("l4d2/props_unique",      "item",       ["personal", "nostalgic"]),

    # Public / institutional architecture
    ("portal2/props_motel",    "retail",     ["public", "abandoned", "mundane"]),
    ("portal2/props_downtown", "retail",     ["public", "mundane"]),
    ("css/props_trainstation", "retail",     ["public", "liminal", "institutional"]),
    ("hl2/props_trainstation", "retail",     ["public", "liminal", "institutional"]),

    # Comforting / nostalgic non-domestic — TF2 thematic maps
    ("tf2/props_diner",        "retail",     ["comforting", "nostalgic", "mundane"]),
    ("tf2/props_camp",         "domestic",   ["comforting", "nostalgic"]),
    ("tf2/props_farm",         "domestic",   ["nostalgic", "comforting", "mundane"]),
    ("tf2/props_manor",        "furniture",  ["nostalgic", "intimate", "comforting"]),
    ("tf2/props_paintings",    "item",       ["personal", "nostalgic"]),
    ("tf2/props_food",         "item",       ["comforting", "mundane"]),

    # Workshop / construction (broaden mundane register)
    ("tf2/props_construction", "workshop",   ["mundane", "abandoned"]),
    ("css/props_industrial",   "workshop",   ["mundane", "institutional"]),
    ("css/props_junk",         "workshop",   ["abandoned", "decayed"]),

    # Wartime / melancholy (DOD untapped)
    ("dod/props_normandy",     "item",       ["melancholy", "abandoned", "nostalgic"]),
    ("dod/props_italian",      "domestic",   ["nostalgic", "melancholy"]),
]

# ---------------------------------------------------------------------------
# 2. Skip patterns — skip files matching these (architectural, effects, etc.)
# ---------------------------------------------------------------------------
SKIP_PATTERNS = [
    r"gib\d*[a-z]?$",       # destruction fragments
    r"_break\d+$",           # breakable variants
    r"_chunk\d*$",
    r"_piece\d*$",
    r"_debris",
    r"attic_beam",
    r"handrail",
    r"conduit_\d",           # raw pipe segments
    r"_trim\d",
    r"_post_",
    r"_underside",
    r"skybox",
    r"^effects",
    r"collision",
    r"^editor",
    r"perftest",
    r"test_",
    r"_lod\d",
    r"_phys$",
    r"^clip_",
    r"_base\d+$",
    r"_upper\d+$",
    r"wall_panel",
    r"_nodraw",
    r"_128$",                 # raw architectural segments
    r"_064$",
    r"_032$",
    r"_256$",
]

SKIP_RE = [re.compile(p, re.IGNORECASE) for p in SKIP_PATTERNS]

def should_skip(stem: str) -> bool:
    for pat in SKIP_RE:
        if pat.search(stem):
            return True
    return False

# ---------------------------------------------------------------------------
# 3. Filename → additional emotional tags
# ---------------------------------------------------------------------------
KEYWORD_EMOTIONAL = [
    # Intimate / personal
    (r"bed|crib|pillow|blanket|mattress|cradle",         ["intimate"]),
    (r"photo|picture|frame|portrait|album",              ["nostalgic", "personal"]),
    (r"toy|doll|teddy|puppet|stuffed",                   ["nostalgic", "personal", "childhood"]),
    (r"book|shelf|bookcase|bookshelf|library",           ["comforting", "nostalgic"]),
    (r"lamp|lantern|candle|light_fixture|nightstand",    ["intimate", "comforting"]),
    (r"couch|sofa|loveseat|armchair",                    ["comforting", "intimate"]),
    (r"bathtub|bath|sink|shower|toilet",                 ["intimate", "mundane"]),
    (r"alarm_clock|clock|watch",                         ["mundane", "personal"]),
    (r"mirror|vanity",                                   ["intimate", "personal"]),
    (r"diary|journal|notebook|calendar",                 ["personal", "nostalgic"]),

    # Nostalgic / memory
    (r"antique|vintage|old|retro|worn",                  ["nostalgic"]),
    (r"suitcase|luggage|bag|backpack",                   ["personal", "nostalgic"]),
    (r"letter|envelope|postcard|mail",                   ["nostalgic", "personal"]),
    (r"piano|guitar|violin|instrument|music",            ["nostalgic", "intimate"]),
    (r"radio|phonograph|gramophone|record",              ["nostalgic", "intimate"]),
    (r"tv|television|monitor|screen",                    ["mundane", "nostalgic"]),

    # Clinical / threatening
    (r"syringe|needle|scalpel|blade|knife|weapon",       ["threatening", "clinical"]),
    (r"morgue|anatomy|specimen|dissect",                 ["clinical", "threatening"]),
    (r"cage|restraint|straitjacket|chains",              ["threatening"]),
    (r"medic|hospital|gurney|stretcher|wheelchair",      ["clinical"]),
    (r"biohazard|hazmat|chemical|toxic",                 ["threatening", "clinical"]),
    (r"operating|surgery|exam",                          ["clinical", "threatening"]),

    # Melancholy / memorial
    (r"grave|coffin|casket|tombstone|hearse|funeral",    ["melancholy"]),
    (r"memorial|monument|shrine|candle_lit",             ["melancholy", "nostalgic"]),
    (r"empty|bare|hollow|broken",                        ["abandoned", "melancholy"]),

    # Abandoned / junk
    (r"trash|garbage|waste|rubbish|junk|debris",         ["abandoned"]),
    (r"rust|decay|rotten|worn|weathered",                ["abandoned", "nostalgic"]),
    (r"cardboard|box|crate|barrel|pallet",               ["abandoned", "mundane"]),

    # Public / institutional
    (r"atm|vending|machine|dispenser",                   ["public", "institutional"]),
    (r"sign|signage|placard|billboard",                  ["public", "mundane"]),
    (r"register|cashregister|counter",                   ["public", "retail"]),
    (r"bench_subway|bench_bus|booth",                    ["public", "mundane"]),
    (r"airport|transit|station|terminal",                ["public", "institutional"]),

    # Mundane / everyday
    (r"table|chair|desk|cabinet|drawer|dresser",         ["mundane", "domestic"]),
    (r"fridge|refrigerator|stove|oven|microwave",        ["mundane", "domestic"]),
    (r"mop|bucket|broom|cleaning",                       ["mundane"]),
    (r"phone|telephone",                                  ["mundane", "personal"]),
    (r"computer|keyboard|monitor|printer|fax",           ["mundane", "institutional"]),

    # Comforting / warmth
    (r"fireplace|hearth|chimney|wood_stove",             ["comforting", "nostalgic"]),
    (r"blanket|quilt|rug|carpet",                        ["comforting", "intimate"]),
    (r"candle|firelight",                                ["comforting", "intimate"]),
    (r"kitchen|cooking|pot|pan|dish",                    ["comforting", "mundane"]),

    # ── PASS 2: New emotional vocabulary ────────────────────────────────────

    # LIMINAL — thresholds, transitional, in-between spaces
    (r"door(?!_handle)|doorway|doorframe",               ["liminal"]),
    (r"hallway|corridor|stairs|stairwell|stairway",      ["liminal"]),
    (r"threshold|archway|gateway|portal_(?!ico)",        ["liminal"]),
    (r"elevator|escalator|ladder|staircase",             ["liminal", "mundane"]),
    (r"window|curtain|blinds(?!ide)",                    ["liminal", "intimate"]),

    # SACRED — ritual, religious, ceremonial
    (r"altar|shrine|cross_(?!hatch)|crucifix|pew",       ["sacred", "melancholy"]),
    (r"church|chapel|cathedral|religious|holy",          ["sacred"]),
    (r"statue(?!ttes)|idol|relic|icon_",                 ["sacred", "nostalgic"]),
    (r"prayer|votive|offering|incense",                  ["sacred", "intimate"]),

    # BUREAUCRATIC — paperwork, admin, records
    (r"filing|file_cabinet|filecabinet|folder|binder",   ["bureaucratic", "institutional"]),
    (r"clipboard|paperwork|document|form_|forms_",       ["bureaucratic", "institutional"]),
    (r"stamp|ledger|receipt|invoice|paperstack",         ["bureaucratic", "mundane"]),
    (r"papers|paperwall|paperstack|paperpile",           ["bureaucratic", "mundane"]),
    (r"typewriter|adding_machine|register_",             ["bureaucratic", "nostalgic"]),

    # DECAYED — rotted, ruined, fallen-apart variants
    (r"rotted|rotten|decayed|crumbling|collapsed",       ["decayed", "abandoned"]),
    (r"ruined|wrecked|burnt|burned|charred",             ["decayed", "abandoned"]),
    (r"corroded|tarnished|moldy|moldering|mossy",        ["decayed", "abandoned"]),
    (r"rusted|rusty|rust_",                              ["decayed", "abandoned"]),

    # Refinement: photo/portrait now also personal+intimate (memory objects)
    (r"family_photo|family_portrait|wedding",            ["personal", "intimate", "nostalgic"]),
]

KEYWORD_RE = [(re.compile(p, re.IGNORECASE), tags) for p, tags in KEYWORD_EMOTIONAL]

def get_emotional_tags_from_name(stem: str) -> list:
    found = set()
    for pat, tags in KEYWORD_RE:
        if pat.search(stem):
            found.update(tags)
    return sorted(found)

# ---------------------------------------------------------------------------
# 4. Visual tag heuristics from filename (preserve existing tag conventions)
# ---------------------------------------------------------------------------
def guess_size_tag(stem: str) -> str:
    large_kw = r"large|big|heavy|grand|double|king|queen|full|wide|long|wardrobe|armoire|cabinet|bookcase|sofa|couch|bed|table|refrigerator|stove|washer|dryer"
    small_kw = r"small|tiny|mini|micro|glass|cup|mug|plate|book|bottle|can|jar|pen|pencil|phone|remote|key|coin|pill|syringe|alarm_clock|candle"
    if re.search(large_kw, stem, re.I):
        return "large"
    if re.search(small_kw, stem, re.I):
        return "small"
    return "medium"

def guess_color_tag(stem: str) -> str:
    warm_kw = r"wood|oak|pine|maple|red|orange|yellow|gold|amber|warm|hearth|fire|candle|bronze|copper"
    cool_kw = r"steel|metal|iron|chrome|blue|silver|white|cold|clinical|glass|plastic|computer|tech"
    dark_kw = r"black|dark|night|shadow|ebony"
    if re.search(dark_kw, stem, re.I):
        return "dark"
    if re.search(warm_kw, stem, re.I):
        return "warm"
    if re.search(cool_kw, stem, re.I):
        return "cool"
    return "neutral"

# ---------------------------------------------------------------------------
# 5. Display name cleanup
# ---------------------------------------------------------------------------
def make_display_name(stem: str) -> str:
    # Remove numeric suffixes like 01a, _02, etc.
    name = re.sub(r'[_\-]?0*\d+[a-z]?$', '', stem)
    name = name.replace('_', ' ').replace('-', ' ').strip()
    return name.title()

# ---------------------------------------------------------------------------
# 6. ID generation
# ---------------------------------------------------------------------------
def make_id(source_dir: str, stem: str) -> str:
    prefix = source_dir.replace('/', '_').replace('-', '_')
    return f"{prefix}_{stem}".lower()

# ---------------------------------------------------------------------------
# 7. Main expansion logic
# ---------------------------------------------------------------------------
def expand_manifest(dry_run: bool = False):
    with open(MANIFEST_PATH) as f:
        manifest = json.load(f)

    existing_paths = {p["glb_path"] for p in manifest["props"]}
    existing_ids = {p["id"] for p in manifest["props"]}
    new_entries = []
    skipped = 0
    already_exists = 0

    for source_dir, group, base_emotional in TARGET_DIRS:
        dir_path = MODELS_ROOT / source_dir
        if not dir_path.exists():
            print(f"  [WARN] Directory not found: {dir_path}")
            continue

        glb_files = sorted(dir_path.glob("*.glb"))
        dir_added = 0

        for glb_file in glb_files:
            stem = glb_file.stem
            rel_path = f"{source_dir}/{glb_file.name}"

            if rel_path in existing_paths:
                already_exists += 1
                continue

            if should_skip(stem):
                skipped += 1
                continue

            entry_id = make_id(source_dir, stem)
            if entry_id in existing_ids:
                already_exists += 1
                continue

            # Build tags
            size_tag = guess_size_tag(stem)
            color_tag = guess_color_tag(stem)
            emotional = get_emotional_tags_from_name(stem)
            extra_emotional = [t for t in base_emotional if t not in emotional]
            emotional_tags = sorted(set(emotional + extra_emotional))

            tags = ["matte", color_tag, size_tag]

            entry = {
                "id": entry_id,
                "glb_path": rel_path,
                "display_name": make_display_name(stem),
                "group": group,
                "category": source_dir.split('/')[-1],
                "tags": tags,
                "emotional_tags": emotional_tags,
                "poly_count": 0,      # unknown without loading the mesh
                "dimensions": {"x": 0.0, "y": 0.0, "z": 0.0},
            }

            new_entries.append(entry)
            existing_ids.add(entry_id)
            existing_paths.add(rel_path)
            dir_added += 1

        print(f"  {source_dir}: +{dir_added} props")

    print(f"\nSummary:")
    print(f"  Existing props:    {len(manifest['props'])}")
    print(f"  Already covered:   {already_exists}")
    print(f"  Skipped (noise):   {skipped}")
    print(f"  New props to add:  {len(new_entries)}")
    print(f"  New total:         {len(manifest['props']) + len(new_entries)}")

    if dry_run:
        print("\n[DRY RUN] No changes written.")
        # Print a sample
        print("\nSample new entries:")
        for e in new_entries[:5]:
            print(json.dumps(e, indent=2))
        return

    # Also backfill emotional_tags on existing props that have none
    backfilled = 0
    for prop in manifest["props"]:
        if "emotional_tags" not in prop:
            stem = Path(prop["glb_path"]).stem
            base = []
            # infer from existing group
            group_defaults = {
                "domestic": ["intimate", "mundane"],
                "furniture": ["comforting", "domestic"],
                "item": ["mundane", "personal"],
                "lab": ["clinical", "institutional"],
                "office": ["mundane", "institutional"],
                "retail": ["public", "mundane"],
                "tech": ["mundane", "institutional"],
                "workshop": ["mundane"],
            }
            base = group_defaults.get(prop.get("group", ""), [])
            from_name = get_emotional_tags_from_name(stem)
            prop["emotional_tags"] = sorted(set(base + from_name))
            backfilled += 1

    manifest["props"].extend(new_entries)
    print(f"  Backfilled emotional_tags on {backfilled} existing props.")

    output_path = "--output" in sys.argv and sys.argv[sys.argv.index("--output") + 1] or MANIFEST_PATH
    with open(output_path, 'w') as f:
        json.dump(manifest, f, separators=(',', ':'))

    print(f"\nWritten to: {output_path}")
    print(f"Final manifest: {len(manifest['props'])} props")


if __name__ == "__main__":
    dry_run = "--dry-run" in sys.argv
    print(f"{'[DRY RUN] ' if dry_run else ''}Expanding manifest...")
    os.chdir(Path(__file__).parent / "Assets/StreamingAssets" / "../..")
    # Change to project dir so relative paths work
    os.chdir(Path(__file__).parent)
    expand_manifest(dry_run)
