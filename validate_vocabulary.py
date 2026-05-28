#!/usr/bin/env python3
"""
Validate vocabulary.json for consistency and coverage.

Checks:
  - All emotional tags are from the valid set of 16
  - Weight values in range
  - No empty entries
  - Tag distribution balance (each tag ≥3%, none >35%)
  - Object/setting values map to known categories
  - Reports coverage gaps

Run from project root:
  python3 validate_vocabulary.py
"""

import json
import sys
from pathlib import Path
from collections import Counter

VOCAB_PATH = Path("Assets/StreamingAssets/vocabulary.json")

VALID_EMOTIONAL = {
    "intimate", "nostalgic", "personal", "comforting", "domestic",
    "clinical", "institutional", "bureaucratic", "threatening",
    "melancholy", "abandoned", "decayed", "sacred", "liminal",
    "public", "mundane"
}

VALID_GROUPS = {"domestic", "furniture", "item", "lab", "office", "retail", "tech", "workshop"}
VALID_SETTINGS = {"home", "office", "workshop", "lab", "retail", "public", "sacred", "liminal"}


def validate():
    if not VOCAB_PATH.exists():
        print(f"ERROR: {VOCAB_PATH} not found")
        return False

    with open(VOCAB_PATH) as f:
        vocab = json.load(f)

    errors = []
    warnings = []

    words = vocab.get("words", {})
    phrases = vocab.get("phrases", {})
    total = len(words)

    print(f"Validating {total} words, {len(phrases)} phrases...\n")

    # --- Check individual entries ---
    tag_counts = Counter()
    for word, entry in {**words, **phrases}.items():
        etags = entry.get("emotional", [])
        weight = entry.get("weight", 0.8)

        if not etags:
            errors.append(f"  '{word}': no emotional tags")
            continue

        invalid = set(etags) - VALID_EMOTIONAL
        if invalid:
            errors.append(f"  '{word}': invalid tags {invalid}")

        if len(etags) > 4:
            warnings.append(f"  '{word}': {len(etags)} tags (max recommended: 4)")

        if not (0.0 <= weight <= 1.0):
            errors.append(f"  '{word}': weight {weight} out of range [0,1]")

        obj = entry.get("object")
        if obj and obj not in VALID_GROUPS:
            warnings.append(f"  '{word}': unknown object group '{obj}'")

        setting = entry.get("setting")
        if setting and setting not in VALID_SETTINGS:
            warnings.append(f"  '{word}': unknown setting '{setting}'")

        for tag in etags:
            if tag in VALID_EMOTIONAL:
                tag_counts[tag] += 1

    # --- Tag distribution ---
    print("Tag distribution:")
    balance_ok = True
    for tag in sorted(VALID_EMOTIONAL):
        count = tag_counts.get(tag, 0)
        pct = 100 * count / total if total > 0 else 0
        status = ""
        if pct < 3:
            status = " << LOW (target ≥3%)"
            balance_ok = False
        elif pct > 35:
            status = " << HIGH (target ≤35%)"
            balance_ok = False
        print(f"  {tag:<20} {count:>5} ({pct:>5.1f}%){status}")

    # --- Report ---
    print(f"\n{'='*50}")
    if errors:
        print(f"\nERRORS ({len(errors)}):")
        for e in errors[:20]:
            print(e)
        if len(errors) > 20:
            print(f"  ... and {len(errors)-20} more")

    if warnings:
        print(f"\nWARNINGS ({len(warnings)}):")
        for w in warnings[:20]:
            print(w)
        if len(warnings) > 20:
            print(f"  ... and {len(warnings)-20} more")

    ok = len(errors) == 0
    print(f"\nResult: {'PASS' if ok else 'FAIL'} — {total} words, {len(errors)} errors, {len(warnings)} warnings")
    if not balance_ok:
        print("  Tag balance: some tags outside target range")

    return ok


if __name__ == "__main__":
    import os
    os.chdir(Path(__file__).parent)
    ok = validate()
    sys.exit(0 if ok else 1)
