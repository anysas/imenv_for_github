from __future__ import annotations

import math
import re
from collections import Counter, defaultdict
from typing import Dict, Iterable, List


TAG_CONTEXT = {
    "engaging": "attention grabbing compelling interactive",
    "sticky": "habit forming repeated return sessions",
    "discoverable": "search browse visibility findability",
    "marketable": "commercial retail showcase product appeal",
    "trend_aligned": "popular current culture zeitgeist",
    "conversion_ready": "call to action purchase intent",
    "retention_friendly": "replay revisit long term interest",
    "shareable": "social screenshot streamable expressive",
    "brand_safe": "clean compliant low risk content",
    "premium_feel": "high fidelity polished refined quality",
    "broad_appeal": "accessible mainstream universal",
    "niche_depth": "specialized subculture distinct identity",
    "monetizable": "commerce upsell bundle collectible",
    "replayable": "variation replay loops return",
    "recommendation_fit": "algorithmic relevance ranking signals",
    "campaign_ready": "promotion launch hero asset",
}


def _tokenize(text: str) -> List[str]:
    return [t for t in re.sub(r"[^a-z0-9_ ]+", " ", text.lower()).split() if t]


def _tf(tokens: Iterable[str]) -> Counter:
    return Counter(tokens)


def _cosine(a: Dict[str, float], b: Dict[str, float]) -> float:
    if not a or not b:
        return 0.0
    dot = sum(a.get(k, 0.0) * b.get(k, 0.0) for k in a.keys())
    na = math.sqrt(sum(v * v for v in a.values()))
    nb = math.sqrt(sum(v * v for v in b.values()))
    if na == 0 or nb == 0:
        return 0.0
    return dot / (na * nb)


def _tag_text(tag: str) -> str:
    extra = TAG_CONTEXT.get(tag, "")
    return f"{tag.replace('_', ' ')} {extra}".strip()


def score_embedding_candidates(profile_text: str, tags: Iterable[str]) -> Dict[str, float]:
    # TF cosine between model profile and tag descriptor context.
    profile_tf = _tf(_tokenize(profile_text))
    out = {}
    for tag in tags:
        tag_tf = _tf(_tokenize(_tag_text(tag)))
        score = _cosine(profile_tf, tag_tf)
        if score > 0:
            out[tag] = score

    if not out:
        return {}
    max_val = max(out.values()) or 1.0
    return {k: min(v / max_val, 1.0) for k, v in out.items()}
