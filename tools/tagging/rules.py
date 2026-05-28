from __future__ import annotations

import re
from collections import defaultdict
from typing import Dict, Iterable, List


# Lightweight deterministic mappings: token pattern -> taxonomy tags.
PERSONAL_RULES = [
    (r"\b(home|house|bedroom|kitchen|nursery|family|childhood|memory|nostalgia)\b", ["domestic", "intimate", "nostalgic", "personal"]),
    (r"\b(church|chapel|altar|prayer|sacred|shrine|temple)\b", ["sacred", "ritualistic", "devotional"]),
    (r"\b(hospital|clinic|medical|ward|surgical|sterile)\b", ["clinical", "institutional"]),
    (r"\b(rust|ruin|abandoned|derelict|broken|decay|decayed)\b", ["abandoned", "decayed", "melancholy"]),
    (r"\b(corridor|hallway|threshold|liminal|doorway|stairwell)\b", ["liminal", "ambiguous"]),
    (r"\b(office|form|paperwork|bureau|queue|compliance)\b", ["bureaucratic", "institutional", "mundane"]),
    (r"\b(play|toy|party|birthday|game)\b", ["playful", "joyful", "nostalgic"]),
    (r"\b(threat|danger|weapon|blood|horror|creepy)\b", ["threatening", "anxious", "eerie"]),
]

CORPORATE_RULES = [
    (r"\b(trend|viral|popular)\b", ["trend_aligned", "shareable", "engaging"]),
    (r"\b(store|retail|market|shop|brand)\b", ["marketable", "conversion_ready", "brand_safe"]),
    (r"\b(game|loop|repeat|session|challenge)\b", ["replayable", "retention_friendly", "sticky"]),
    (r"\b(curated|premium|high[-_ ]?quality|hero)\b", ["premium_feel", "campaign_ready"]),
    (r"\b(recommend|discover|feed|search)\b", ["discoverable", "recommendation_fit"]),
    (r"\b(niche|specialist|subculture)\b", ["niche_depth"]),
    (r"\b(monetize|sale|upsell|bundle)\b", ["monetizable", "conversion_ready"]),
    (r"\b(accessible|friendly|mainstream|universal)\b", ["broad_appeal"]),
]


def build_profile_text(prop: Dict) -> str:
    chunks = [
        prop.get("id", ""),
        prop.get("display_name", ""),
        prop.get("category", ""),
        prop.get("group", ""),
        prop.get("glb_path", ""),
        " ".join(prop.get("tags", []) or []),
        " ".join(prop.get("custom_tags", []) or []),
        prop.get("notes", "") or "",
    ]
    text = " ".join(str(c) for c in chunks if c).lower()
    return re.sub(r"[^a-z0-9_ ]+", " ", text)


def score_rule_candidates(profile_text: str, prop: Dict | None = None) -> Dict[str, Dict[str, float]]:
    personal_scores = defaultdict(float)
    corporate_scores = defaultdict(float)

    for pattern, tags in PERSONAL_RULES:
        matches = re.findall(pattern, profile_text)
        if not matches:
            continue
        weight = 0.18 + (0.05 * min(len(matches), 5))
        for tag in tags:
            personal_scores[tag] += weight

    for pattern, tags in CORPORATE_RULES:
        matches = re.findall(pattern, profile_text)
        if not matches:
            continue
        weight = 0.16 + (0.05 * min(len(matches), 5))
        for tag in tags:
            corporate_scores[tag] += weight

    # Metadata heuristics for coverage even when names are sparse.
    if prop is not None:
        group = str(prop.get("group", "")).strip().lower()
        size = str(prop.get("size_category", "")).strip().lower()

        if group in {"retail", "office"}:
            corporate_scores["discoverable"] += 0.45
            corporate_scores["marketable"] += 0.4
            corporate_scores["conversion_ready"] += 0.3
        if group in {"item", "domestic", "furniture"}:
            corporate_scores["broad_appeal"] += 0.35
            corporate_scores["brand_safe"] += 0.25
            corporate_scores["recommendation_fit"] += 0.25
        if group in {"tech", "lab"}:
            corporate_scores["trend_aligned"] += 0.35
            corporate_scores["engaging"] += 0.25
            corporate_scores["shareable"] += 0.2
        if group in {"workshop"}:
            corporate_scores["niche_depth"] += 0.35
            corporate_scores["premium_feel"] += 0.2

        if size in {"small", "medium"}:
            corporate_scores["replayable"] += 0.2
            corporate_scores["retention_friendly"] += 0.2

    return {
        "personal": dict(personal_scores),
        "corporate": dict(corporate_scores),
    }


def normalize_scores(scores: Dict[str, float]) -> Dict[str, float]:
    if not scores:
        return {}
    max_val = max(scores.values()) or 1.0
    return {k: min(v / max_val, 1.0) for k, v in scores.items()}
