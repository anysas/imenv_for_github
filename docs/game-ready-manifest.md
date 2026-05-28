# Game-ready vs full curated manifest

The sandbox can load a **smaller manifest** that only includes props you have **manually reviewed** in the curation flow, while the full `curated-props.json` remains the authoring source of truth.

## Definition

A prop is **game-ready** when:

- Its `id` appears in `reviewed_ids` in [`Assets/StreamingAssets/curation_overrides.json`](../Assets/StreamingAssets/curation_overrides.json), and  
- Its `id` is **not** in `removed_ids` in the same file, and  
- It exists as a row in [`Assets/StreamingAssets/curated-props.json`](../Assets/StreamingAssets/curated-props.json).

All other rows from `curated-props.json` go into the **non-ready** split file (reference / tooling only).

## Regenerate split files

From the repo root:

```bash
python3 tools/split_game_ready_manifest.py
```

Outputs:

| File | Purpose |
|------|---------|
| `Assets/StreamingAssets/curated-props.game-ready.json` | Runtime sandbox manifest (when enabled) |
| `Assets/StreamingAssets/curated-props.non-ready.json` | Everything else (not loaded by sandbox) |
| `curation-reports/game_ready_split_<timestamp>.json` | Counts + integrity (duplicate ids, missing GLBs, reviewed ids not in base) |

Options:

- `--no-glb-check` — skip verifying each game-ready `glb_path` exists under `Assets/StreamingAssets/models/`
- `--manifest` / `--overlay` — alternate input paths
- `--out-game-ready` / `--out-non-ready` — alternate output paths

## Runtime behaviour

[`SandboxManager`](../Assets/scripts/SandboxManager.cs) (sandbox scene):

- **`Prefer Game Ready Manifest`** (default: on) — if `curated-props.game-ready.json` exists and loads with at least one prop, it is used.
- If the file is missing, empty, or invalid, the manager **falls back** to `curated-props.json` and logs a warning.
- **`Fallback Manifest File Name`** — usually `curated-props.json`.

Loader API: [`CuratedPropManifest.LoadFromStreamingAssets(string fileName)`](../Assets/scripts/CuratedPropManifest.cs) and `CuratedPropManifest.ManifestPath(...)`.

**Curation scene** and other tools that call `LoadFromStreamingAssets()` with no arguments still load the **full** `curated-props.json` unless you change them.

## Rollback

1. In the Inspector on `SandboxManager`, disable **Prefer Game Ready Manifest**, or  
2. Delete / rename `curated-props.game-ready.json` so the runtime falls back automatically.

## Build size reduction (safe prune)

After generating your keep-list manifest, prune unreferenced files under `Assets/StreamingAssets/models`:

```bash
python3 tools/prune_streaming_models.py
python3 tools/prune_streaming_models.py --include-live-sessions
python3 tools/prune_streaming_models.py --apply
```

What this does:

- Uses `Assets/StreamingAssets/curated-props.game-ready.json` as the keep list by default.
- Also preserves GLBs referenced by hallway session JSON files:
  - `Assets/StreamingAssets/seed_sessions/` (default)
  - optional live sessions in `~/Library/Application Support/.../Evil Minecraft/sessions` via `--include-live-sessions`
- Finds all `.glb` files not referenced by that manifest.
- In `--apply` mode, moves unreferenced `.glb` files (and `.meta` sidecars) to an external archive folder:
  - `/Users/ezrajohnston/Desktop/unity/_asset_archives/<project>_prune_<timestamp>/models/...`
- Writes a report to `curation-reports/model_prune_<timestamp>.json`.

Optional flags:

- `--manifest <path>` to prune by a different shipped manifest.
- `--archive-root <path>` to choose archive destination.
- `--session-dir <path>` (repeatable) to include custom session JSON directories in keep-set.
- `--no-seed-sessions` to ignore seed session refs.
- `--hard-delete --apply` to permanently delete instead of archive-move.
- `--skip-meta` to leave `.meta` files untouched.

## Prune rollback and permanent purge

- **Rollback after archive-move:** move archived files back into `Assets/StreamingAssets/models` (same relative paths), then reopen Unity.
- **Permanent deletion (after QA):** delete the archive folder itself, or rerun prune with `--hard-delete --apply`.
- **Sanity check:** rerun `python3 tools/prune_streaming_models.py` and confirm `unreferenced_glb=0` before building.
