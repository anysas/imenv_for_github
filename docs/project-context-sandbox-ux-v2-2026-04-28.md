# Algorithmic Gallery v2 - Sandbox UX Context (2026-04-28)

## Purpose
This document captures the current state and intent of the **Algorithmic Gallery v2 sandbox flow** before implementing the UX fixes listed in the April 28 brief.

It is written as a handoff context for both collaborators:
- **Ezra**: systems, scripts, gameplay orchestration
- **Alyssa**: environment, visual UX, scene composition

## Project Snapshot
- Course: Immersive Environments (A3 + A4)
- Runtime: Unity 6 + URP 17
- Working scene: `Assets/Scenes/Trial1.unity`
- Tone: PSX-era jank, visible quantization/dither/glitch escalation
- Play length: ~2 minutes
- Core narrative arc: user agency is gradually overwritten by a helpful-to-controlling automated assistant

## Experience Flow
1. Player starts in a hallway and enters sandbox room.
2. Player places curated props.
3. Assistant profiles placement style and ramps influence over ~90 seconds.
4. Assistant transitions from helpful to overbearing, eventually filling space based on inferred preference.
5. End card appears after the timed session.

## Runtime Architecture (V2)
Primary scripts in `Assets/scripts/` under `AlgorithmicGallery.Corruption`:
- `SandboxBootstrap.cs`: creates/ensures `SandboxManager`.
- `SandboxManager.cs`: orchestrator, session timing, event hub (`OnSandboxEntered`, `OnSessionComplete`).
- `CuratedPropManifest.cs`: loads `Assets/StreamingAssets/curated-props.json` with metadata (including dimensions).
- `StyleProfile.cs`: records player/assistant placements and style signals.
- `HotbarController.cs`: 5-slot inventory with reroll-on-consume behavior.
- `HotbarUI.cs`: runtime-built UGUI, no prefab dependency.
- `PropPlacer.cs`: floor raycast placement + delete interactions.
- `AssistantSystem.cs`: influence curve, phase logic, autonomous/reactive assistant placement.
- `PropBudget.cs`: max prop cap and eviction strategy.
- `RuntimeThumbnailCapture.cs`: asynchronous thumbnail sprite generation.

Shared legacy dependency:
- `Assets/scripts/SculptureSpawner.cs` in `AlgorithmicGallery` namespace, used to load GLB models via GLTFast.

## Current Behavior in `Trial1.unity`
Working:
- Bootstrap wiring, runtime system creation, placement/destroy loop.
- Hotbar interactions and scroll cycling.
- Session timeout and end-card progression.
- PSX post-process influence ramp.

Broken or missing for this pass:
1. Prop scale variance is lost (models are normalized to fixed max dimension).
2. Assistant activates too early (first placement + immediate autonomous pacing).
3. Hotbar displays before sandbox entry.
4. No clear control instructions on screen.
5. Ghost preview is placeholder cube, not active prop mesh.

## Critical Constraints
- Keep V1 script behavior intact where possible (especially `SculptureSpawner` default behavior for existing callers).
- Preserve URP compatibility requirement for current PSX pass (RenderGraph disabled).
- Avoid broad architecture refactors during UX pass; changes should be local and reversible.
- Keep runtime behavior deterministic enough for quick playtest iteration in `Trial1.unity`.

## Design Intent for This Pass
- Make scale feel grounded in real-world object variance.
- Delay assistant intervention so users first feel creative control.
- Present UI only when contextually relevant (in sandbox, not hallway).
- Improve affordance clarity with minimal visual clutter.
- Improve placement readability by previewing the actual active prop model.

## Non-Goals
- Rewriting renderer feature for RenderGraph mode.
- Replacing current session timing model.
- Editing curated dataset content.
- Reworking hotbar reroll logic.

## Dependencies Between Fixes
- Scaling update in `SculptureSpawner` is prerequisite for model-accurate ghost preview.
- Hotbar reveal gating should precede hint text integration so both use one visibility control path.

## Success Criteria
- Player can infer controls immediately from on-screen hints.
- Props read as plausible real-world objects in relative size.
- Assistant does not interfere before placement threshold.
- Hotbar and hint UI only appear when sandbox interaction starts.
- Ghost preview reflects selected prop mesh and respects world placement target.
