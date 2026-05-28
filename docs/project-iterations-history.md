# Immersive Environments — Iteration History

Last updated: 2026-05-06

This document explains how the project evolved from the original gallery prototype to the current v2 sandbox/corruption system, including the major technical and design pivots.

## 1) High-Level Evolution

The project has gone through three major eras:

1. **Foundational v1 gallery era** (gaze/recommendation driven)
2. **Transition and stabilization era** (room loop attempts, then fallback simplification)
3. **v2 corruption era** (player-building sandbox + assistant takeover), followed by large-scale curation/manifest expansion

## 2) Chronological Timeline

### 2026-04-12 — Context and baseline consolidation

- First formal context and handoff docs were consolidated.
- Initial architecture snapshot focused on `Assets/scripts` organization and active vs experimental systems.
- Reference docs:
  - `docs/project-context-2026-04-12.md`
  - `docs/development-update-2026-04-12.md`
  - `docs/unity-scene-integration-checklist-2026-04-12.md`
- Related transcript: [April 12 context handoff](ccf826ac-ecc9-4a19-8a09-338fb0303fbe)

### 2026-04-14 (Session 1) — Growth/decay overhaul

- v1 growth behavior shifted to continuous growth/decay with persistent stubs.
- Vine/tendril-style generation expanded and broader model ingestion was explored.
- Related transcript: [Growth decay vine plan](096dae7c-95f3-4bb1-9f6a-7880d23e8232)

### 2026-04-14 (Session 2) — Stability and filtering hardening

- Endless-room prototype work continued but with safeguards and legacy-safe startup.
- Filtering moved toward stronger category/path/dimension constraints.
- Randomization and bounds behavior were adjusted to improve demo reliability.
- Related transcript: [Filters demo room stability](fdbdfbd3-fbab-4673-83ae-e1a838f2a617)

### 2026-04-16 — Room-loop stabilization attempts and fallback decision

- Multiple revisions attempted to stabilize `RoomLoopManager` and room chaining.
- Outcome: room loop remained unstable (overlap/drift), so legacy/non-loop path was treated as the stable baseline.
- Related docs:
  - `docs/session-log.md`
  - `docs/linear-room-loop-editor-setup-2026-04-16.md`
  - `docs/room-chain-stabilization-checklist-2026-04-16.md`
- Related transcript: [Room loop context analysis](8e43f0d1-c8c0-4a0b-821d-6bf85934cb2e)

### 2026-04-16 (later sessions) — Interaction polish and linear trigger path

- Diegetic interaction response work: gaze/proximity behavior tuning, smoother transitions, emission compatibility fixes.
- Linear trigger-driven progression work was used as a simpler alternative to procedural room chaining.
- Related transcript: [Sculpture interaction linear demo](e4fe5611-c01b-49df-9ec3-ea8ad1ed5f0d)

### 2026-04-25 to 2026-04-28 — Major pivot to v2 Corruption sandbox

- Concept pivot solidified: from passive recommendation/gaze to active placement where assistant gradually overrides player agency.
- v2 system stack was built around:
  - `SandboxManager`, `AssistantSystem`, `HotbarController`, `PropPlacer`, `StyleProfile`
  - new curated prop manifest (`curated-props.json`)
  - PSX renderer feature/passes
  - session export and end card framing
- Core docs for this phase:
  - `docs/gap-analysis-2026-04-25.md`
  - `docs/project-brief-updated-2026-04-26.md`
  - `docs/development-update-2026-04-26.md`
  - `docs/development-plan-2026-04-26.md`
  - `docs/setup-v2.md`
  - `docs/project-context-sandbox-ux-v2-2026-04-28.md`
  - `docs/sandbox-ux-fixes-implementation-plan-2026-04-28.md`
  - `docs/analysis-terminal-screen-plan-2026-04-28.md`
  - `docs/psx-reactive-vfx-plan-2026-04-28.md`
- Related transcript: [Sandbox UX docs plan](5686aaba-3395-4b45-927f-0e9b3038e705)

### 2026-04-29 onward — Integration/debug passes

- Iterative fixes around VFX integration, compile/runtime issues, and UX polish.
- Related transcripts:
  - [VFX inputs compile fixes](5648348b-5c8b-4583-aeec-35b4f70a1c87)
  - [Docs presentation prototype slides](3d11ae07-c4ac-4937-a756-3bbb1a547b65)

### 2026-05-05 to 2026-05-06 — Curation and manifest expansion era

- Focus shifted strongly to creative quality of placement experience.
- Prompted by issue: semantically linked random props still felt disconnected from creative intent.
- Decisions and outcomes:
  - move away from pre-made prompt options toward fully text-driven intent
  - invest in larger usable prop pool from full model corpus
  - enforce practical constraints (floor-placeable, size-limited, tagged)
  - build and use bulk ingestion + validation scripts
  - remove oversized entries from active manifest
- Related transcript: [Curation manifest expansion May](68f0a562-077b-4a15-848a-e904fd77a8ec)

## 3) Major Pivots and Why They Happened

## Pivot A — Procedural room-loop ambition vs stability reality

- **Intent:** dynamic chained rooms with directional progression.
- **Issue:** high fragility (room overlap/drift and runtime unpredictability).
- **Decision:** keep loop experiments in repo but rely on stable alternatives for demo flow.

## Pivot B — v1 passive attention model to v2 active agency model

- **Intent shift:** from “observe + recommend” to “build + be overridden.”
- **Why:** stronger alignment with course framing around AI mediation, misinterpretation, and loss of agency.
- **Technical consequence:** new core systems were introduced (hotbar/placement/profile/influence curve/PSX escalation/session export), while v1 code was retained as legacy reference.

## Pivot C — small curated set to aggressive pool expansion

- **Intent:** increase material diversity and improve player expression potential.
- **Constraint:** additions still had to be placeable and practical in-session.
- **Technical consequence:** Python-driven expansion and validation pipeline became central to content iteration.

## 4) Versioned “Eras” by System

### Era 1 — v1 Attention Gallery (legacy)

- Dominant components:
  - `GalleryManager`
  - gaze providers (`DesktopGazeProvider`, `VRGazeProvider`, `GazeManager`)
  - recommendation strategies (`Exploration`, `Exploitation`, `Desperation`)
  - room/door loop components
- Strength: conceptually coherent recommendation/gaze framework
- Limitation: unstable procedural room loop for production demo context

### Era 2 — Transition and hardening

- Added guardrails, fixed filtering/performance/input issues, explored linear progression alternatives.
- Codebase remained hybrid, with both legacy and experimental room systems present.

### Era 3 — v2 Corruption Sandbox (current active model)

- Dominant components:
  - `SandboxManager`
  - `AssistantSystem`
  - `PropPlacer`
  - `HotbarController` / `HotbarUI`
  - `StyleProfile`
  - `SessionExporter`
  - PSX renderer feature stack
- Content backbone:
  - `curated-props.json`
  - `curate_pipeline.py`
  - `expand_manifest.py`
  - `remove_oversized_from_manifest.py`
- Curation tooling:
  - `CurationManager`, `CurationUI`, `CurationViewport`, `CurationOverlay`

## 5) Current Open Threads (Post-Iteration)

- Continued curation quality pass (semantic/emotional tag accuracy and creative coherence)
- Hallway/past-session framing UX maturity
- End-card branching variants tied to player engagement pattern
- Ongoing balancing between pool size and meaningfulness (avoid “more props” becoming “more noise”)

## 6) History Sources Used

Primary authored docs:

- `docs/session-log.md`
- `docs/project-context-2026-04-12.md`
- `docs/development-update-2026-04-12.md`
- `docs/development-plan-2026-04-26.md`
- `docs/development-update-2026-04-26.md`
- `docs/project-brief-updated-2026-04-26.md`
- `docs/gap-analysis-2026-04-25.md`
- `docs/setup-v2.md`
- `docs/project-context-sandbox-ux-v2-2026-04-28.md`
- `docs/sandbox-ux-fixes-implementation-plan-2026-04-28.md`
- `docs/analysis-terminal-screen-plan-2026-04-28.md`
- `docs/psx-reactive-vfx-plan-2026-04-28.md`
- `docs/linear-room-loop-editor-setup-2026-04-16.md`
- `docs/room-chain-stabilization-checklist-2026-04-16.md`
- `docs/unity-scene-integration-checklist-2026-04-12.md`

Related transcript references:

- [April 12 context handoff](ccf826ac-ecc9-4a19-8a09-338fb0303fbe)
- [Growth decay vine plan](096dae7c-95f3-4bb1-9f6a-7880d23e8232)
- [Filters demo room stability](fdbdfbd3-fbab-4673-83ae-e1a838f2a617)
- [Room loop context analysis](8e43f0d1-c8c0-4a0b-821d-6bf85934cb2e)
- [Sculpture interaction linear demo](e4fe5611-c01b-49df-9ec3-ea8ad1ed5f0d)
- [Sandbox UX docs plan](5686aaba-3395-4b45-927f-0e9b3038e705)
- [VFX inputs compile fixes](5648348b-5c8b-4583-aeec-35b4f70a1c87)
- [Docs presentation prototype slides](3d11ae07-c4ac-4937-a756-3bbb1a547b65)
- [Curation manifest expansion May](68f0a562-077b-4a15-848a-e904fd77a8ec)

