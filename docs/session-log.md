# Algorithmic Gallery Session Log

This is an append-only running log of project sessions.
Do not replace prior sections; always add new dated entries to the end.

## 2026-04-12

- **Focus:** project context and handoff consolidation for `Assets/scripts`.
- **Outputs:** foundational context docs and implementation status snapshot.
- **Transcript:** [Project Context Handoff](ccf826ac-ecc9-4a19-8a09-338fb0303fbe)

## 2026-04-14 (Session 1)

- **Focus:** growth/decay behavior overhaul, vine-chain sculpture generation expansion, and content ingestion from broader model set.
- **Key outcomes:**
  - continuous growth/decay and persistent stub behavior
  - larger, more spread vine-like sculpture generation
  - docs updated to reflect stabilized architecture and priorities
- **Transcript:** [Growth Decay Expansion](096dae7c-95f3-4bb1-9f6a-7880d23e8232)

## 2026-04-14 (Session 2)

- **Focus:** endless-room prototype implementation, fallback to stable legacy gallery flow for class demo, filtering/performance/input fixes.
- **Key outcomes:**
  - room system scripts added and gated (legacy-safe startup)
  - content filtering moved to stronger path/category + dimensions approach
  - candidate randomization fixed to avoid game-order bias
  - sculpture bounds clamped to room-friendly limits
  - idle update performance improved, stub visibility initialization fixed
  - macOS mouse sensitivity fix in SUPER Character Controller
- **Transcript:** [Room Filter Stability Pass](fdbdfbd3-fbab-4673-83ae-e1a838f2a617)

## 2026-04-16

- **Focus:** linear room-loop stabilization attempts (direction lock, overlap prevention, trigger-anchored chaining, and editor wiring verification).
- **Key outcomes:**
  - implemented multiple `RoomLoopManager` and `RoomRuntime` revisions to force single-direction spawning and strict anchor/trigger contracts
  - hardened `GalleryManager` async pedestal ownership to prevent stale sculpture spawns
  - runtime behavior remains unstable in-scene (rooms still reported overlapping/drifting instead of consistent direct-line chaining)
  - session closed with room-loop work marked unresolved; treat legacy/non-loop gallery flow as the only stable path until a clean redesign pass
- **Transcript:** [Room Loop Stabilization Attempts](8e43f0d1-c8c0-4a0b-821d-6bf85934cb2e)

## 2026-04-16 (Session 2)

- **Focus:** sculpture interaction pass for diegetic attention feedback (gaze/proximity/touch), emission reliability, smoothing, and non-blocking traversal.
- **Key outcomes:**
  - fixed GLTFast shader property/keyword compatibility (`emissiveFactor`, `baseColorFactor`, `_EMISSIVE`)
  - removed lean-toward-player behavior and retained model base materials (stopped base color overwrite in runtime MPB path)
  - added smoothed growth/proximity response and eased `GrowthPart` transitions (`SmoothDamp`) for less jittery inflation/deflation
  - enforced stronger cross-pedestal diversity by reserving selected parts across pedestals
  - resolved traversal blocking by making sculpture root colliders triggers and disabling imported descendant colliders on spawned parts
  - reduced mesh emission intensity to a subtle range and added a simple world-space halo ring (`LineRenderer`) as primary attention cue
- **Open follow-ups:**
  - if silhouette-style outline/stroke is desired (instead of ground ring), add an outline shader/pass in a dedicated rendering pass
  - tune halo width/alpha/emission caps against final lighting/post stack for exhibition mode
- **Transcript:** [Diegetic Sculpture Responses](e4fe5611-c01b-49df-9ec3-ea8ad1ed5f0d)

## 2026-04-16 (Session 3)

- **Focus:** linear non-generated 10-room demo stabilization using existing room triggers, while disabling procedural room-loop handoff.
- **Key outcomes:**
  - reworked `LinearGalleryController` to use `RoomRuntime` + `RoomDoorTrigger` events for room progression
  - hard-disabled managed room generation handoff (`RoomLoopManager` / `LegacyRoomTransitionTrigger`) in linear demo flow
  - enforced strict room-0 pedestal targeting support (`firstpedestal1` / `firstpedestal2`) with fallback discovery path
  - added detailed `RoomDoorTrigger` debug logs to diagnose trigger entry, tag filtering, and notify path
  - fixed startup spawn race in `GalleryManager` by gating pedestal spawns on recommender/pool readiness
  - reduced noisy stale async spawn warnings to normal logs (expected churn during slot token changes)
- **Known issue at close:**
  - legacy room 0 remained inconsistent versus prefab rooms; recommended production path is replacing legacy room 0 with a standard prefab room and starting player before it
- **Transcript:** [Linear Trigger Debug Pass](e4fe5611-c01b-49df-9ec3-ea8ad1ed5f0d)

## Notes for future sessions

- Use this file as the single chronological index.
- Keep detailed technical change notes in:
  - `docs/development-update-2026-04-12.md`
  - `docs/project-context-2026-04-12.md`
- Add a new dated section per session and include its transcript link.
