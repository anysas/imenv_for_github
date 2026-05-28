# Algorithmic Gallery - Project Context (April 12, 2026, updated April 16, 2026)

## Purpose
This document is a concise project handoff snapshot for **Immersive Environments** and the **Algorithmic Gallery** implementation as of **April 12, 2026**.  
It combines intended design direction with the current state of `Assets/scripts` so future work can prioritize the right fixes.

## Project Intent
- Interactive gallery where sculptures bloom under gaze and decay when ignored.
- Recommendation engine intentionally becomes more manipulative over time to critique attention-retention systems.
- Three emotional phases:
  - **Fascination** (mostly exploration)
  - **Recognition** (more exploitation + adjacent probing)
  - **Unease** (aggressive exploitation + occasional disruptive swings)
- Desktop-first prototype, VR optional (Quest 3), fully offline/local runtime.
- Key timeline: **Prototype due April 16**, **Final due May 28**.

## Runtime Architecture
- `GalleryManager`: Orchestrates metadata loading, recommendation calls, and pedestal spawning.
- `GazeManager` + gaze providers:
  - `DesktopGazeProvider` (center-screen camera ray)
  - `VRGazeProvider` (HMD forward ray with fallback)
- `SculptureSpawner`: Loads `.glb` assets via GLTFast from `StreamingAssets/models`.
- `SculptureController`: Continuous growth/decay controller with:
  - persistent stub baseline (sculptures remain visible at rest)
  - distance-aware attention response (subtle far reaction, full bloom only near)
  - per-instance transform/material modulation via `MaterialPropertyBlock`
- `GrowthPart`: Per-part reveal system (threshold-based progressive emergence/retraction).
- Recommendation core (`AlgorithmicGallery.Recommendation` namespace):
  - `MetadataIndex`, `UserProfile`, `RecommendationEngine`
  - `ExplorationStrategy`, `ExploitationStrategy`, `DesperationStrategy`
- `RecommendationDebugUI`: IMGUI debug overlay.

## Data and Content Pipeline Context
- Offline pipeline (Blender/Python) exports `.glb`, auto-tags source assets, and builds unified `metadata.json`.
- Unity runtime expects:
  - `Assets/StreamingAssets/metadata.json`
  - `Assets/StreamingAssets/models/<game>/*.glb`
- Asset source scope includes Source Engine game collections (Portal/Portal2/HL2/TF2 target set).
- Runtime filtering now supports either:
  - all games (default, `_useAllowedGameFilter = false`)
  - explicit game allow-list (`_useAllowedGameFilter = true`, `_allowedGames` populated)

## Current Status (From `Assets/scripts` Reality Check)

### Implemented and structurally coherent
- Core gameplay loop exists: gaze tracking, dwell handling, recommendation selection, async model load, and spawn orchestration.
- Recommendation phase model and strategies are implemented in separate classes.
- Metadata indexing and tag-based scoring/filtering are present.
- Sculptures now preserve structural identity across time (no normal decay-driven replacement).
- Generation now uses a vine-chain tendril composition model (trunk + branching tendrils), not the earlier layer-scatter approach.

### Important mismatches and risks in current code
- Room-generation scripts now exist, but are intentionally demo-gated:
  - `RoomLoopManager` is opt-in at startup (`_enabledAtStart = false`) so legacy gallery flow remains stable by default.
- Large blended sculptures can still challenge GPU/CPU depending on lighting and part count; bounds clamping and lower defaults reduce but do not eliminate cost.
- `SculptureSpawner` and `GalleryManager` use direct file APIs for `StreamingAssets`; this is fine for desktop, but Quest/Android will require `UnityWebRequest`-based loading.
- Sculpture visuals now target GLTFast-compatible emission/base color properties, but final perceived intensity still depends on post-processing configuration (Bloom/Tonemapping) and scene lighting balance.
- Outline-like emphasis currently uses a simple ground halo (`LineRenderer`) and subtle mesh emission; full silhouette stroke/outline is not implemented yet.
- Mixed legacy/prefab linear-room setups are fragile; room 0 without `RoomRuntime` introduces edge cases in pedestal binding and trigger progression.
- Content quality is now heavily dependent on filter tuning (allow/deny keywords + dimensions) and should be iterated in inspector for each showcase environment.

### Content/data readiness caveat
- `metadata.json` is now filtered at runtime by:
  - path/category allow-list and deny-list
  - name deny-list
  - poly threshold
  - metadata dimension and volume thresholds
- Recommendation candidate order is shuffled before strategy pooling to avoid source-order game bias.

## Immediate Priority Order
1. Stabilize final sculpture interaction language for showcase:
   - tune halo/emission balance per attention state (gaze/proximity/touch)
   - decide whether to keep ground halo or implement silhouette outline shader
2. Finalize room traversal reliability:
   - maintain non-blocking sculpture colliders across all spawn paths
   - validate trigger flow across all chained rooms
   - remove legacy room 0 from linear chain (use prefab room 0 + player start before entry trigger)
3. Add deterministic seed controls:
   - expose session seed in `GalleryManager` for reproducible playtests.
4. Add budgeted rendering/perf tiers:
   - active-part caps and quality modes
   - collider simplification options
5. Continue room-generation redesign as a separate track:
   - treat legacy/non-loop gallery flow as stable baseline until loop architecture is reworked

## Definition of "Prototype Ready" (April 16)
- Multi-game model pool can load from `StreamingAssets`.
- Gaze causes visible distance-sensitive growth/decay with persistent stubs.
- Recommendation phase progression is visible in debug UI.
- Sculptures preserve identity while regrowing from stub state.
- Stable desktop build runs locally without internet dependency.

## Key Source References
- `Assets/scripts/SCRIPTS_MANIFEST.txt`
- `Assets/scripts/RECOMMENDATION_ENGINE_PORT.txt`
- `Assets/scripts/` (current implementation source of truth)
