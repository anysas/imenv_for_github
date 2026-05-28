"""Soft-uniform tag selection with confidence guardrails (online or static floor/cap)."""
from __future__ import annotations

from typing import Dict, List, Optional, Tuple


def _dynamic_floor_cap(
    processed_models: int,
    max_n: int,
    vocab_len: int,
    floor_ratio: float,
    cap_ratio: float,
) -> Tuple[int, int]:
    """Uniform expectation after `processed_models` each contributing up to max_n slots."""
    if vocab_len <= 0 or processed_models <= 0:
        return 0, 10**9
    target_per_tag = (processed_models * max_n) / vocab_len
    floor = max(0, int(floor_ratio * target_per_tag))
    cap = max(floor + 1, int(cap_ratio * target_per_tag + 1))
    return floor, cap


def _static_floor_cap(
    n_models: int,
    max_n: int,
    vocab_len: int,
    floor_ratio: float,
    cap_ratio: float,
) -> Tuple[int, int]:
    if vocab_len <= 0 or n_models <= 0:
        return 0, 10**9
    target_per_tag = (n_models * max_n) / vocab_len
    floor = max(1, int(floor_ratio * target_per_tag))
    cap = max(floor + 1, int(cap_ratio * target_per_tag + 1))
    return floor, cap


def choose_tags_soft_uniform(
    scored: Dict[str, float],
    min_conf: float,
    max_n: int,
    global_counts: Dict[str, int],
    vocab: List[str],
    floor_ratio: float,
    cap_ratio: float,
    strength: float,
    *,
    processed_models: Optional[int] = None,
    floor_cap_override: Optional[Tuple[int, int]] = None,
    n_models_static: Optional[int] = None,
) -> Tuple[List[str], List[str], float, Dict]:
    """
    Pick up to max_n tags from `scored` with score >= min_conf (same eligibility as choose_tags).
    Returns (balanced_picked, natural_picked, max_raw_score, trace).
    """
    if strength <= 0.0:
        ordered = sorted(scored.items(), key=lambda kv: kv[1], reverse=True)
        max_raw = ordered[0][1] if ordered else 0.0
        natural = [k for k, v in ordered if v >= min_conf][:max_n]
        return natural[:], natural[:], max_raw, {"balance_mode": "strength_zero", "balance_changed_pick": False}

    vocab_set = set(vocab)
    eligible = [(k, float(v)) for k, v in scored.items() if k in vocab_set and v >= min_conf]
    eligible.sort(key=lambda kv: kv[1], reverse=True)
    max_raw = eligible[0][1] if eligible else 0.0

    natural_picked: List[str] = []
    for k, _ in eligible:
        if len(natural_picked) >= max_n:
            break
        natural_picked.append(k)

    vn = len(vocab)
    if floor_cap_override is not None:
        floor, cap = floor_cap_override
    elif n_models_static is not None:
        floor, cap = _static_floor_cap(n_models_static, max_n, vn, floor_ratio, cap_ratio)
    else:
        floor, cap = _dynamic_floor_cap(processed_models or 0, max_n, vn, floor_ratio, cap_ratio)

    picked: List[str] = []
    local_counts = {t: int(global_counts.get(t, 0)) for t in vocab}

    def adjusted(tag: str, raw: float) -> float:
        c = local_counts.get(tag, 0)
        bonus = strength * max(0, floor - c)
        penalty = strength * max(0, c - cap)
        return raw + bonus - penalty

    pool = eligible[:]
    pool.sort(key=lambda kv: adjusted(kv[0], kv[1]), reverse=True)
    for k, _raw in pool:
        if len(picked) >= max_n:
            break
        if k in picked:
            continue
        picked.append(k)
        local_counts[k] = local_counts.get(k, 0) + 1

    trace = {
        "dynamic_floor": floor,
        "dynamic_cap": cap,
        "strength": strength,
        "natural_picked": natural_picked[:],
        "balanced_picked": picked[:],
        "balance_changed_pick": set(natural_picked) != set(picked),
    }
    return picked, natural_picked, max_raw, trace
