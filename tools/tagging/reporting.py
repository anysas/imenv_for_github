from __future__ import annotations

import csv
import json
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple


def _personal_tags_prop(p: Dict) -> List[str]:
    emo = [str(t).strip().lower() for t in (p.get("emotional_tags") or []) if str(t).strip()]
    per = [str(t).strip().lower() for t in (p.get("personal_tags") or []) if str(t).strip()]
    return sorted(set(emo) | set(per))


def _corporate_tags_prop(p: Dict) -> List[str]:
    return [str(t).strip().lower() for t in (p.get("corporate_tags") or []) if str(t).strip()]


def _distributions_from_props(props: List[Dict]) -> Tuple[Counter, Counter]:
    pd: Counter = Counter()
    cd: Counter = Counter()
    for p in props:
        pd.update(_personal_tags_prop(p))
        cd.update(_corporate_tags_prop(p))
    return pd, cd


def summarize(results: List[Dict]) -> Dict:
    total = len(results)
    with_personal = sum(1 for r in results if r.get("final_personal_tags"))
    with_corporate = sum(1 for r in results if r.get("final_corporate_tags"))
    added_personal = sum(len(r.get("added_personal_tags") or []) for r in results)
    added_corporate = sum(len(r.get("added_corporate_tags") or []) for r in results)

    personal_dist = Counter()
    corporate_dist = Counter()
    for r in results:
        personal_dist.update(r.get("final_personal_tags") or [])
        corporate_dist.update(r.get("final_corporate_tags") or [])

    return {
        "total_models": total,
        "coverage_personal_pct": round((with_personal / total * 100.0) if total else 0.0, 2),
        "coverage_corporate_pct": round((with_corporate / total * 100.0) if total else 0.0, 2),
        "added_personal_total": added_personal,
        "added_corporate_total": added_corporate,
        "top_personal_tags": personal_dist.most_common(20),
        "top_corporate_tags": corporate_dist.most_common(20),
        "full_personal_tag_counts": dict(sorted(personal_dist.items())),
        "full_corporate_tag_counts": dict(sorted(corporate_dist.items())),
    }


def _gate_summary(
    taxonomy: Dict[str, List[str]],
    gate_params: Dict[str, Any],
    personal_dist: Counter,
    corporate_dist: Counter,
    n_models: int,
) -> Dict[str, Any]:
    """Per-tag deficit/surplus vs floor/cap using full manifest or result-set counts."""
    floor_ratio = float(gate_params.get("floor_ratio") or 0.25)
    cap_ratio = float(gate_params.get("cap_ratio") or 2.5)
    max_personal = int(gate_params.get("max_personal") or 8)
    max_corporate = int(gate_params.get("max_corporate") or 4)

    personal_vocab = taxonomy.get("personal_tags") or []
    corporate_vocab = taxonomy.get("corporate_tags") or []

    pd = personal_dist
    cd = corporate_dist

    def detail(vocab: List[str], dist: Counter, max_n: int) -> Dict[str, Any]:
        vn = len(vocab) or 1
        target = (n_models * max_n) / vn if n_models else 0.0
        floor = max(1, int(floor_ratio * target)) if n_models else 0
        cap = int(cap_ratio * target + 1) if n_models else 0
        per = {}
        below = []
        above = []
        for t in vocab:
            c = int(dist.get(t, 0))
            per[t] = {"count": c, "target": round(target, 2), "floor": floor, "cap": cap}
            if c < floor:
                below.append(t)
            if c > cap:
                above.append(t)
        return {
            "target_per_tag": round(target, 4),
            "floor": floor,
            "cap": cap,
            "tags_below_floor": sorted(below),
            "tags_above_cap": sorted(above),
            "per_tag": per,
        }

    return {
        "personal": detail(personal_vocab, pd, max_personal),
        "corporate": detail(corporate_vocab, cd, max_corporate),
    }


def evaluate_tag_gates(
    manifest_props: List[Dict],
    taxonomy: Dict[str, List[str]],
    floor_ratio: float,
    cap_ratio: float,
    max_personal: int,
    max_corporate: int,
    *,
    enforce_cap: bool = True,
) -> tuple[bool, Dict[str, Any]]:
    """Return (all_ok, gate_block) for full manifest tag distribution."""
    pd, cd = _distributions_from_props(manifest_props)
    n = len(manifest_props)
    params = {
        "floor_ratio": floor_ratio,
        "cap_ratio": cap_ratio,
        "max_personal": max_personal,
        "max_corporate": max_corporate,
        "n_models": n,
    }
    block = _gate_summary(taxonomy, params, pd, cd, n)
    bp = block["personal"]["tags_below_floor"]
    bc = block["corporate"]["tags_below_floor"]
    ok = len(bp) == 0 and len(bc) == 0
    if enforce_cap:
        ok = ok and len(block["personal"]["tags_above_cap"]) == 0 and len(block["corporate"]["tags_above_cap"]) == 0
    block["all_gates_pass"] = ok
    return ok, block


def write_reports(
    report_base: Path,
    run_meta: Dict,
    results: List[Dict],
    taxonomy: Optional[Dict[str, List[str]]] = None,
    gate_params: Optional[Dict[str, Any]] = None,
    manifest_props: Optional[List[Dict]] = None,
) -> None:
    report_base.parent.mkdir(parents=True, exist_ok=True)
    summary = summarize(results)

    gate_block: Optional[Dict[str, Any]] = None
    if taxonomy and gate_params is not None:
        if manifest_props is not None:
            pd_full, cd_full = _distributions_from_props(manifest_props)
            n_gate = len(manifest_props)
        else:
            pd_full = Counter()
            cd_full = Counter()
            for r in results:
                pd_full.update(r.get("final_personal_tags") or [])
                cd_full.update(r.get("final_corporate_tags") or [])
            n_gate = int(gate_params.get("n_models") or len(results))
        gate_block = _gate_summary(taxonomy, gate_params, pd_full, cd_full, n_gate)

    json_payload: Dict[str, Any] = {
        "meta": run_meta,
        "summary": summary,
        "results": results,
    }
    if gate_block is not None:
        json_payload["tag_gates"] = gate_block

    report_base.with_suffix(".json").write_text(json.dumps(json_payload, indent=2))

    with report_base.with_suffix(".csv").open("w", newline="") as f:
        w = csv.writer(f)
        w.writerow(
            [
                "id",
                "display_name",
                "kept_personal",
                "added_personal",
                "final_personal",
                "kept_corporate",
                "added_corporate",
                "final_corporate",
                "max_personal_score",
                "max_corporate_score",
                "review_reasons",
                "pass_b_applied",
            ]
        )
        for r in results:
            w.writerow(
                [
                    r.get("id", ""),
                    r.get("display_name", ""),
                    " ".join(r.get("kept_personal_tags", [])),
                    " ".join(r.get("added_personal_tags", [])),
                    " ".join(r.get("final_personal_tags", [])),
                    " ".join(r.get("kept_corporate_tags", [])),
                    " ".join(r.get("added_corporate_tags", [])),
                    " ".join(r.get("final_corporate_tags", [])),
                    f"{r.get('max_personal_score', 0.0):.4f}",
                    f"{r.get('max_corporate_score', 0.0):.4f}",
                    ";".join(r.get("review_reasons") or []),
                    str(bool(r.get("pass_b_applied"))),
                ]
            )

    review_rows = [r for r in results if r.get("review_reasons")]
    if review_rows:
        rq = report_base.with_name(report_base.name + "_review_queue").with_suffix(".csv")
        with rq.open("w", newline="") as f:
            w = csv.writer(f)
            w.writerow(
                [
                    "id",
                    "display_name",
                    "review_reasons",
                    "final_personal",
                    "final_corporate",
                    "max_personal_score",
                    "max_corporate_score",
                    "balance_trace_personal",
                    "balance_trace_corporate",
                ]
            )
            for r in review_rows:
                w.writerow(
                    [
                        r.get("id", ""),
                        r.get("display_name", ""),
                        ";".join(r.get("review_reasons") or []),
                        " ".join(r.get("final_personal_tags", [])),
                        " ".join(r.get("final_corporate_tags", [])),
                        f"{r.get('max_personal_score', 0.0):.4f}",
                        f"{r.get('max_corporate_score', 0.0):.4f}",
                        json.dumps(r.get("balance_trace_personal") or {}),
                        json.dumps(r.get("balance_trace_corporate") or {}),
                    ]
                )
