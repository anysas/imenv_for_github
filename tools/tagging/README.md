# Auto-tagging tools

All commands assume repo root as the current working directory.

## Baseline (read-only)

Per-tag counts and deficit vs uniform target on the current manifest:

```bash
python3 tools/tagging/baseline_report.py
```

Options: `--manifest`, `--taxonomy`, `--floor-ratio`, `--cap-ratio`, `--max-personal`, `--max-corporate`, `--out`.

## Auto-tagging

Dry run (writes `curation-reports/tagging_dryrun_*.json/.csv`):

```bash
python3 tools/tagging/run_tagging.py
python3 tools/tagging/run_tagging.py --balance-mode soft_uniform --balance-pass-b
```

Apply to `Assets/StreamingAssets/curated-props.json`:

```bash
python3 tools/tagging/run_tagging.py --apply --balance-mode soft_uniform
```

Useful flags:

| Flag | Default | Notes |
|------|---------|--------|
| `--balance-mode` | `none` | `soft_uniform` re-ranks picks using global floor/cap shaping |
| `--balance-strength` | `0.02` | Set `0` to disable shaping (same as natural ranking) |
| `--balance-floor-ratio` | `0.25` | Floor vs uniform target |
| `--balance-cap-ratio` | `2.5` | Cap vs uniform target |
| `--balance-pass-b` | off | Second pass with static floor/cap from full catalog size |
| `--review-confidence` | `0.55` | Flags review queue when max blended score is below this |
| `--limit` | 0 | Process first N props only |

Reports include `tag_gates` (full manifest distribution vs floor/cap) and `*_review_queue.csv` when any row has `review_reasons`.

## Gate check

```bash
python3 tools/tagging/check_gates.py
python3 tools/tagging/check_gates.py --ignore-cap   # floor only
```

Exit `0` when all tags are within floor (and cap unless `--ignore-cap`).

## Convergence loop (apply + gates)

```bash
python3 tools/tagging/run_convergence.py --max-iterations 5 --balance-pass-b
```

Dry tagging + gate check only (manifest unchanged):

```bash
python3 tools/tagging/run_convergence.py --dry-only
```

After automated passes, use the review queue CSV in `curation-reports/` for targeted fixes in the curation scene, then re-run.
