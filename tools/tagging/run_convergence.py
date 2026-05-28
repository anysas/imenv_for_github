#!/usr/bin/env python3
"""
Run auto-tagging with soft-uniform balancing repeatedly until check_gates passes
or max iterations. Uses manifest on disk between steps (no manual review loop here).
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
RUN_TAGGING = REPO_ROOT / "tools/tagging/run_tagging.py"
CHECK_GATES = REPO_ROOT / "tools/tagging/check_gates.py"


def main() -> int:
    p = argparse.ArgumentParser(description="Iterate tagging + gate check until pass or max iterations")
    p.add_argument("--max-iterations", type=int, default=5)
    p.add_argument("--balance-pass-b", action="store_true", help="Forward to run_tagging")
    p.add_argument("--ignore-cap", action="store_true", help="Forward to check_gates")
    p.add_argument("--dry-only", action="store_true", help="Only run final dry tagging + gates (no apply)")
    args = p.parse_args()

    py = sys.executable

    def run_tagging(apply: bool) -> None:
        cmd = [
            py,
            str(RUN_TAGGING),
            "--balance-mode",
            "soft_uniform",
            "--min-confidence",
            "0.42",
        ]
        if args.balance_pass_b:
            cmd.append("--balance-pass-b")
        if apply:
            cmd.append("--apply")
        subprocess.check_call(cmd, cwd=str(REPO_ROOT))

    def run_gates() -> int:
        cmd = [py, str(CHECK_GATES)]
        if args.ignore_cap:
            cmd.append("--ignore-cap")
        return subprocess.call(cmd, cwd=str(REPO_ROOT))

    if args.dry_only:
        run_tagging(apply=False)
        return run_gates()

    for i in range(1, args.max_iterations + 1):
        print(f"[convergence] iteration {i}/{args.max_iterations}: apply tagging…")
        run_tagging(apply=True)
        code = run_gates()
        if code == 0:
            print(f"[convergence] gates passed after {i} iteration(s).")
            print("[convergence] running final dry run for report…")
            run_tagging(apply=False)
            return 0
        print(f"[convergence] gates not satisfied (exit {code}), continuing…")

    print("[convergence] max iterations reached; gates may still fail. Tune strength/floor or review queue.")
    run_tagging(apply=False)
    return run_gates()


if __name__ == "__main__":
    raise SystemExit(main())
