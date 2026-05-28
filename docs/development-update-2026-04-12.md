# Algorithmic Gallery Development Update (April 12, 2026 + Session Extension April 14, 2026)

This document summarizes implementation changes made during integration and playtesting, plus next development priorities.

## Session extension (April 14, 2026)

### Core interaction model updates (implemented)
- Replaced timer-state bloom/decay behavior with a continuous growth model in `SculptureController`:
  - growth increases while gazed, decays when ignored
  - sculptures now keep a persistent visible stub floor instead of disappearing
  - distance-aware growth gates full bloom at long range and allows subtle response from afar
- Added `GrowthPart` component for per-part reveal/retreat behavior using reveal thresholds.
- Updated `GazeManager` to send continuous gaze state and gaze distance to sculptures.

### Identity and persistence updates (implemented)
- Removed recycle-on-decay behavior:
  - sculptures are no longer destroyed/replaced as part of normal decay
  - each sculpture keeps its specific generated structure/identity over time
  - looking away wilts to stub; looking back regrows the same structure

### Sculpture generation updates (implemented)
- Replaced layer-scatter assembly with vine-chain tendril growth in `GalleryManager`:
  - trunk-core seeding
  - tendril extension model-by-model
  - branching/forking with depth limits
  - directional orientation using tendril direction
- Increased aggressive authoring defaults for larger/more spread compositions:
  - higher `_modelsPerSculpture`
  - larger trunk radius/height and part scales
  - higher tendril count/forking/spread
  - global sculpture scale multiplier

### Content availability updates (implemented)
- Added optional game filter toggle in `GalleryManager`:
  - `_useAllowedGameFilter = false` by default, so all available games can be used when files exist on disk.
- Runtime logs now clearly report whether filtering is active (`allowedGames=[ALL]` vs explicit list).

### Additional maintenance
- Fixed compile warning in `SculptureSpawner` by removing unused `_generateColliders`.

## Session close (April 14, 2026 - class demo stabilization)

### Priority reset to stable gallery demo
- `RoomLoopManager` was made opt-in at startup:
  - `_enabledAtStart = false` by default
  - legacy `GalleryManager` pedestal flow now works without room-system interference
- Added legacy bridge support for future transition:
  - `BeginManagedLoop()` / `BeginManagedLoopFrom(...)` in `RoomLoopManager`
  - `LegacyRoomTransitionTrigger` to move from legacy room to prefab loop when needed

### Content filtering overhaul (implemented)
- Reworked filtering toward player-facing folders/categories:
  - allow-list centered on `props`, `characters`, `items`, `weapons`, `vehicles`, `furniture`, `player`, `survivors`, `infected`, etc.
  - deny-list expanded for technical/uninteresting buckets (`editor`, `shadertest`, `perftest`, `skybox`, `anim_wp`, map-editor artifacts, etc.)
- Added strong environment/oversize suppression:
  - deny terms for building/terrain-style categories (`building`, `bridge`, `highway`, `sewer`, `canal`, etc.)
  - dimension-based model rejection using metadata:
    - max axis threshold
    - max volume threshold
    - reject `scale=monumental`
- Candidate list is now shuffled before strategy pooling to avoid source-order game bias.

### Sculpture size containment (implemented)
- Reduced aggressive defaults to fit gallery rooms better:
  - lower `_modelsPerSculpture`
  - lower `_tendrilMaxHeight`
  - lower `_globalSculptureScale`
- Added hard assembled-sculpture clamp in `GalleryManager`:
  - max final sculpture height
  - max XZ radius
  - blend root is downscaled after assembly if bounds exceed limits

### Performance stabilization (implemented)
- `SculptureController` now avoids redundant per-frame updates when growth is stable.
- `GrowthPart` now avoids redundant transform and renderer state writes.
- Net effect: idle sculptures do far less work; active gaze targets still animate correctly.

### Stub visibility fix (implemented)
- Added explicit startup synchronization so stubs are visible immediately at spawn.
- `GrowthPart` now supports `SnapToGrowth(...)`, called from `SculptureController` on enable/refresh.

### Mac input fix (implemented)
- Patched SUPER Character Controller mouse delta scaling for macOS in the Input System path.
- Added platform-specific divisor adjustment to avoid unusable sensitivity on Mac.

## What was implemented

### Compile/runtime compatibility fixes
- Replaced deprecated `FindObjectOfType<T>()` with `FindFirstObjectByType<T>()`.
- Fixed `RecommendationDebugUI` to use `UserProfile.PreferenceWeights` (not `Preferences`).
- Updated GLTFast loading path to `InstantiateMainSceneAsync(container.transform)`.
- Removed unused debug field that generated warnings.

### Stability and diagnostics
- Added safeguards for missing pedestal slot arrays.
- Added startup diagnostics in `GalleryManager`:
  - allowed models
  - loadable-on-disk models
  - eligible recommender pool count
- Added retry throttling to avoid log spam when no loadable candidates are available.

### Metadata/recommendation candidate handling
- Added `_allowedGames` filter (currently set to `portal` by default).
- Pre-filtered recommender candidates so only models with valid game + existing file path are eligible.
- Added optional randomized initial tag preferences to bootstrap first-session variety.

### Runtime model spawn improvements
- Added automatic runtime normalization/scaling for loaded GLBs (`SculptureSpawner`).
- Added optional centering/grounding pass so loaded model bounds align to pedestal origin.
- Fixed collider/component creation order:
  - ensure collider exists before adding `SculptureController` (`RequireComponent(Collider)` compatibility).
- Added async safety checks to avoid destroyed-object exceptions after awaits.

### Pedestal and sculpture generation changes
- Replaced single-model pedestal spawn with multi-part blend assembly:
  - configurable `_modelsPerSculpture` (target ~40)
  - one root collider + one `SculptureController` per blend
- Placeholder cleanup behavior:
  - disable placeholder renderers/colliders
  - remove placeholder child objects from slot transform on first live spawn

### Visual growth behavior updates
- Added per-instance procedural bloom variation in `SculptureController`:
  - directional stretch
  - drift
  - twist
  - pulse variance
- Updated sculpture blend layout from clustered spiral to tree-like generation:
  - trunk/core section
  - branch layers with outward tilt
  - canopy spread
- Grounds blended sculpture root to floor/pedestal origin after assembly.

### Gaze interaction improvements
- Increased gaze exit grace period for composite sculptures.
- Switched hit test from narrow `Raycast` to `SphereCast` for better target retention.
- Added grace-period cancel behavior when gaze returns to same target before timeout.

## Current known issues / observations
- Editor warning: `Can't Generate Mesh, No Font Asset has been assigned.` appears unrelated to core spawn logic.
- URP shadow atlas warnings indicate lighting cost pressure; performance tuning still needed for stable frame rate.
- Large part counts can still stress runtime depending on selected models and light setup.
- New vine-chain generation can still produce occasional overlap/clumping for specific model shape combinations; spacing/occupancy constraints are the next likely refinement if needed.

## Session extension (April 16, 2026 - room-loop stabilization attempts, unresolved)

### What was attempted
- Refactored room chaining toward explicit trigger/anchor alignment behavior in `RoomLoopManager` and `RoomRuntime`.
- Added stricter runtime validation and serialized-field compatibility handling for room trigger/anchor bindings.
- Added async generation token checks in `GalleryManager` to avoid stale pedestal ownership during room transitions.
- Added and iterated editor setup/checklist docs for linear room loop wiring and runtime verification.

### Observed runtime failure pattern
- Room loop still reported as unstable in live scene testing:
  - rooms spawning with directional drift and overlap
  - inconsistent chaining relative to expected straight-line forward progression
  - user-reported behavior remained unacceptable despite multiple logic passes
- Logs also showed handoff wiring issues in some runs (`LegacyRoomTransitionTrigger: _spawnPointOverride is required but missing. Handoff aborted.`), indicating scene contract fragility.

### Current status decision
- Room-loop feature is **not considered stable** as of this session close.
- Recommended operational baseline remains legacy/stable gallery flow until room generation is redesigned and revalidated from a minimal, testable contract.

## Recommended next development steps
1. Implement room generation system (next session priority):
   - create procedural room/prefab layout flow
   - auto-place/assign pedestal anchors per room
   - establish room-to-room progression and player routing choices
2. Add deterministic seed controls:
   - expose session seed in `GalleryManager` for reproducible playtests.
3. Add anti-overlap controls for tendril placement:
   - minimum spacing checks between placed parts
   - occupancy/grid-based rejection in dense zones
4. Add LOD/perf controls:
   - cap active/rendered parts by budget tiers
   - optional simplified collider strategy per blend.
5. Implement shader/material pipeline pass:
   - ensure all loaded parts use materials that respect `_Saturation`, `_EmissionPower`, `_DissolveAmount`.

## Inspector parameters to tune first
- `GalleryManager`:
  - `_modelsPerSculpture`
  - `_trunkCount`, `_trunkRadius`, `_trunkHeight`
  - `_tendrilCount`, `_tendrilStepSize`, `_tendrilCurvature`, `_forkChance`, `_maxForkDepth`
  - `_tendrilOutwardBias`, `_tendrilUpwardBias`, `_placementSpreadMultiplier`, `_globalSculptureScale`
  - `_useAllowedGameFilter` and `_allowedGames`
- `SculptureSpawner`:
  - `_targetMaxDimension`
  - `_groundYOffset`
- `GazeManager`:
  - `_gazeExitGracePeriod`
  - `_gazeSphereCastRadius`
  - `_closeRangeDistance`, `_farRangeDistance`, `_gazeDirectionSmoothing`
- `SculptureController`:
  - `_stubGrowthLevel`
  - `_distantMaxGrowth`, `_closeRangeDistance`, `_farRangeDistance`
