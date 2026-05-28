#!/usr/bin/env python3
from __future__ import annotations

import argparse
import copy
from collections import Counter
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Set, Tuple

from balance import choose_tags_soft_uniform
from io_manifest import load_manifest, save_manifest
from taxonomy import load_taxonomy
from rules import build_profile_text, score_rule_candidates, normalize_scores
from embedding_ranker import score_embedding_candidates
from reporting import write_reports


REPO_ROOT = Path(__file__).resolve().parents[2]
MANIFEST_PATH = REPO_ROOT / "Assets/StreamingAssets/curated-props.json"
TAXONOMY_PATH = REPO_ROOT / "Assets/StreamingAssets/tag-taxonomy-v1.json"
REPORT_DIR = REPO_ROOT / "curation-reports"


def merge_scores(rule_scores: Dict[str, float], emb_scores: Dict[str, float], wr: float = 0.55, we: float = 0.45) -> Dict[str, float]:
    out = {}
    keys = set(rule_scores.keys()) | set(emb_scores.keys())
    for k in keys:
        out[k] = (wr * rule_scores.get(k, 0.0)) + (we * emb_scores.get(k, 0.0))
    return out


def choose_tags(scored: Dict[str, float], min_conf: float, max_n: int) -> Tuple[List[str], float]:
    ordered = sorted(scored.items(), key=lambda kv: kv[1], reverse=True)
    kept = [k for k, v in ordered if v >= min_conf][:max_n]
    max_score = ordered[0][1] if ordered else 0.0
    return kept, max_score


def _personal_set(p: Dict) -> Set[str]:
    emo = [str(t).strip().lower() for t in (p.get("emotional_tags") or []) if str(t).strip()]
    per = [str(t).strip().lower() for t in (p.get("personal_tags") or []) if str(t).strip()]
    return set(emo) | set(per)


def _corporate_set(p: Dict) -> Set[str]:
    return {str(t).strip().lower() for t in (p.get("corporate_tags") or []) if str(t).strip()}


def _count_all_personal(props: List[Dict]) -> Counter:
    c: Counter = Counter()
    for p in props:
        c.update(_personal_set(p))
    return c


def _count_all_corporate(props: List[Dict]) -> Counter:
    c: Counter = Counter()
    for p in props:
        c.update(_corporate_set(p))
    return c


def _subtract_model_from_counters(dst: Dict, gp: Counter, gc: Counter) -> None:
    for t in _personal_set(dst):
        gp[t] -= 1
    for t in _corporate_set(dst):
        gc[t] -= 1


def _add_model_to_counters(personal: List[str], corporate: List[str], gp: Counter, gc: Counter) -> None:
    for t in personal:
        gp[t] += 1
    for t in corporate:
        gc[t] += 1


def _score_prop(
    src: Dict,
    taxonomy: Dict,
    personal_vocab: Set[str],
    corporate_vocab: Set[str],
) -> Tuple[Dict[str, float], Dict[str, float]]:
    profile = build_profile_text(src)
    rule = score_rule_candidates(profile, src)
    rule_personal = normalize_scores({k: v for k, v in rule["personal"].items() if k in personal_vocab})
    rule_corporate = normalize_scores({k: v for k, v in rule["corporate"].items() if k in corporate_vocab})
    emb_personal = score_embedding_candidates(profile, taxonomy["personal_tags"])
    emb_corporate = score_embedding_candidates(profile, taxonomy["corporate_tags"])
    final_personal_scores = merge_scores(rule_personal, emb_personal)
    final_corporate_scores = merge_scores(rule_corporate, emb_corporate)
    return final_personal_scores, final_corporate_scores


def _apply_tags_to_dst(
    dst: Dict,
    existing_personal: List[str],
    existing_corporate: List[str],
    picked_personal: List[str],
    picked_corporate: List[str],
) -> Tuple[List[str], List[str]]:
    final_personal = sorted(set(existing_personal) | set(picked_personal))
    final_corporate = sorted(set(existing_corporate) | set(picked_corporate))
    dst["emotional_tags"] = final_personal
    dst["personal_tags"] = final_personal
    dst["corporate_tags"] = final_corporate
    custom_tags = [str(t).strip().lower() for t in (dst.get("custom_tags") or []) if str(t).strip()]
    custom_tags = sorted(set(custom_tags) | {f"corp:{t}" for t in final_corporate})
    dst["custom_tags"] = custom_tags
    return final_personal, final_corporate


def _review_reasons(
    review_conf: float,
    max_personal: float,
    max_corporate: float,
    trace_p: Dict,
    trace_c: Dict,
) -> List[str]:
    reasons: List[str] = []
    if max_personal < review_conf:
        reasons.append("low_confidence_personal")
    if max_corporate < review_conf:
        reasons.append("low_confidence_corporate")
    if trace_p.get("balance_changed_pick"):
        reasons.append("balance_changed_personal")
    if trace_c.get("balance_changed_pick"):
        reasons.append("balance_changed_corporate")
    return reasons


def run(args: argparse.Namespace) -> int:
    taxonomy = load_taxonomy(TAXONOMY_PATH)
    root, props = load_manifest(MANIFEST_PATH)
    work = props[: args.limit] if args.limit else props

    results = []
    updated = copy.deepcopy(props)
    updated_by_id = {p.get("id"): p for p in updated}

    personal_vocab = set(taxonomy["personal_tags"])
    corporate_vocab = set(taxonomy["corporate_tags"])
    personal_list = taxonomy["personal_tags"]
    corporate_list = taxonomy["corporate_tags"]

    global_p = _count_all_personal(updated)
    global_c = _count_all_corporate(updated)
    n_catalog = len(updated)

    def process_one(
        dst: Dict,
        src: Dict,
        idx: int,
        *,
        static_pass: bool,
    ) -> Dict:
        _subtract_model_from_counters(dst, global_p, global_c)

        final_personal_scores, final_corporate_scores = _score_prop(
            src, taxonomy, personal_vocab, corporate_vocab
        )

        existing_personal = sorted(_personal_set(dst))
        existing_corporate = sorted(_corporate_set(dst))

        if args.balance_mode == "soft_uniform":
            kw = dict(
                floor_ratio=args.balance_floor_ratio,
                cap_ratio=args.balance_cap_ratio,
                strength=args.balance_strength,
            )
            if static_pass:
                picked_personal, natural_p, max_personal, trace_p = choose_tags_soft_uniform(
                    final_personal_scores,
                    args.min_confidence,
                    args.max_personal,
                    dict(global_p),
                    personal_list,
                    n_models_static=n_catalog,
                    **kw,
                )
                picked_corporate, natural_c, max_corporate, trace_c = choose_tags_soft_uniform(
                    final_corporate_scores,
                    args.min_confidence,
                    args.max_corporate,
                    dict(global_c),
                    corporate_list,
                    n_models_static=n_catalog,
                    **kw,
                )
            else:
                picked_personal, natural_p, max_personal, trace_p = choose_tags_soft_uniform(
                    final_personal_scores,
                    args.min_confidence,
                    args.max_personal,
                    dict(global_p),
                    personal_list,
                    processed_models=idx,
                    **kw,
                )
                picked_corporate, natural_c, max_corporate, trace_c = choose_tags_soft_uniform(
                    final_corporate_scores,
                    args.min_confidence,
                    args.max_corporate,
                    dict(global_c),
                    corporate_list,
                    processed_models=idx,
                    **kw,
                )
        else:
            picked_personal, max_personal = choose_tags(final_personal_scores, args.min_confidence, args.max_personal)
            picked_corporate, max_corporate = choose_tags(final_corporate_scores, args.min_confidence, args.max_corporate)
            natural_p, natural_c = picked_personal[:], picked_corporate[:]
            trace_p = {"balance_changed_pick": False, "natural_picked": natural_p, "balanced_picked": picked_personal[:]}
            trace_c = {"balance_changed_pick": False, "natural_picked": natural_c, "balanced_picked": picked_corporate[:]}

        final_personal, final_corporate = _apply_tags_to_dst(
            dst, existing_personal, existing_corporate, picked_personal, picked_corporate
        )
        _add_model_to_counters(final_personal, final_corporate, global_p, global_c)

        return {
            "picked_personal": picked_personal,
            "picked_corporate": picked_corporate,
            "natural_personal_picks": natural_p,
            "natural_corporate_picks": natural_c,
            "max_personal": max_personal,
            "max_corporate": max_corporate,
            "trace_p": trace_p,
            "trace_c": trace_c,
            "final_personal": final_personal,
            "final_corporate": final_corporate,
            "existing_personal": existing_personal,
            "existing_corporate": existing_corporate,
        }

    # --- Pass A (online dynamic floor/cap) ---
    for i, src in enumerate(work, start=1):
        pid = src.get("id", "")
        dst = updated_by_id.get(pid)
        if dst is None:
            continue
        out = process_one(dst, src, i, static_pass=False)

        kept_personal = sorted(set(out["existing_personal"]))
        kept_corporate = sorted(set(out["existing_corporate"]))
        results.append(
            {
                "id": pid,
                "display_name": src.get("display_name", ""),
                "kept_personal_tags": kept_personal,
                "kept_corporate_tags": kept_corporate,
                "pass_b_applied": False,
                "added_personal_tags": sorted(set(out["final_personal"]) - set(out["existing_personal"])),
                "final_personal_tags": out["final_personal"],
                "added_corporate_tags": sorted(set(out["final_corporate"]) - set(out["existing_corporate"])),
                "final_corporate_tags": out["final_corporate"],
                "max_personal_score": out["max_personal"],
                "max_corporate_score": out["max_corporate"],
                "balance_trace_personal": out["trace_p"],
                "balance_trace_corporate": out["trace_c"],
                "natural_personal_picks": out["natural_personal_picks"],
                "natural_corporate_picks": out["natural_corporate_picks"],
                "balanced_personal_picks": out["picked_personal"],
                "balanced_corporate_picks": out["picked_corporate"],
            }
        )

    # --- Pass B: static floor/cap over full catalog size ---
    if args.balance_mode == "soft_uniform" and args.balance_pass_b:
        results_by_id = {r["id"]: r for r in results}
        for i, src in enumerate(work, start=1):
            pid = src.get("id", "")
            dst = updated_by_id.get(pid)
            if dst is None:
                continue
            before_p = sorted(_personal_set(dst))
            before_c = sorted(_corporate_set(dst))
            process_one(dst, src, i, static_pass=True)
            fp = sorted(_personal_set(dst))
            fc = sorted(_corporate_set(dst))
            if pid in results_by_id:
                row = results_by_id[pid]
                row["final_personal_tags"] = fp
                row["final_corporate_tags"] = fc
                row["added_personal_tags"] = sorted(set(fp) - set(before_p))
                row["added_corporate_tags"] = sorted(set(fc) - set(before_c))
                row["pass_b_applied"] = True

    # Deficit tags (global after this run) for review hints
    def _deficit_tags(counter: Counter, vocab: List[str], max_n: int, floor_ratio: float) -> List[str]:
        n = n_catalog
        vn = len(vocab) or 1
        target = (n * max_n) / vn
        floor = max(1, int(floor_ratio * target)) if n else 0
        return sorted([t for t in vocab if counter.get(t, 0) < floor])

    deficit_p = _deficit_tags(global_p, personal_list, args.max_personal, args.balance_floor_ratio)
    deficit_c = _deficit_tags(global_c, corporate_list, args.max_corporate, args.balance_floor_ratio)

    for r in results:
        r["review_reasons"] = _review_reasons(
            args.review_confidence,
            r["max_personal_score"],
            r["max_corporate_score"],
            r.get("balance_trace_personal") or {},
            r.get("balance_trace_corporate") or {},
        )

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    mode = "apply" if args.apply else "dryrun"
    report_base = REPORT_DIR / f"tagging_{mode}_{stamp}"
    write_reports(
        report_base,
        run_meta={
            "mode": mode,
            "min_confidence": args.min_confidence,
            "max_personal": args.max_personal,
            "max_corporate": args.max_corporate,
            "limit": args.limit or 0,
            "processed": len(results),
            "balance_mode": args.balance_mode,
            "balance_floor_ratio": args.balance_floor_ratio,
            "balance_cap_ratio": args.balance_cap_ratio,
            "balance_strength": args.balance_strength,
            "balance_pass_b": bool(args.balance_pass_b),
            "review_confidence": args.review_confidence,
            "deficit_personal_tags": deficit_p,
            "deficit_corporate_tags": deficit_c,
        },
        results=results,
        taxonomy=taxonomy,
        gate_params={
            "floor_ratio": args.balance_floor_ratio,
            "cap_ratio": args.balance_cap_ratio,
            "max_personal": args.max_personal,
            "max_corporate": args.max_corporate,
            "n_models": n_catalog,
        },
        manifest_props=updated,
    )

    if args.apply:
        save_manifest(MANIFEST_PATH, root, updated)
        print(f"[tagging] APPLY complete. wrote manifest: {MANIFEST_PATH}")
    else:
        print("[tagging] DRY RUN complete. manifest unchanged.")

    print(f"[tagging] reports: {report_base}.json/.csv")
    if any(r.get("review_reasons") for r in results):
        print(f"[tagging] review queue: {report_base.name}_review_queue.csv")
    return 0


def build_arg_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Auto-assign personal/corporate tags to model manifest.")
    p.add_argument("--apply", action="store_true", help="Write assignments back to curated-props.json")
    p.add_argument("--dry-run", action="store_true", help="Explicit dry-run mode (default)")
    p.add_argument("--limit", type=int, default=0, help="Only process first N props")
    p.add_argument("--min-confidence", type=float, default=0.42, help="Minimum blended score to assign a tag")
    p.add_argument("--max-personal", type=int, default=8, help="Maximum added personal tags per model")
    p.add_argument("--max-corporate", type=int, default=4, help="Maximum added corporate tags per model")
    p.add_argument(
        "--balance-mode",
        choices=("none", "soft_uniform"),
        default="none",
        help="soft_uniform re-ranks high-confidence picks using global tag counts",
    )
    p.add_argument("--balance-floor-ratio", type=float, default=0.25, help="Floor vs uniform target (N*max/tags)")
    p.add_argument("--balance-cap-ratio", type=float, default=2.5, help="Cap vs uniform target")
    p.add_argument("--balance-strength", type=float, default=0.02, help="Weight for floor/cap adjustment (0 disables shaping)")
    p.add_argument(
        "--balance-pass-b",
        action="store_true",
        help="Second pass with static floor/cap from full catalog size (soft_uniform only)",
    )
    p.add_argument(
        "--review-confidence",
        type=float,
        default=0.55,
        help="Max score below this threshold flags the row for human review",
    )
    return p


if __name__ == "__main__":
    parser = build_arg_parser()
    args = parser.parse_args()
    if args.apply and args.dry_run:
        parser.error("Choose only one of --apply or --dry-run")
    raise SystemExit(run(args))
