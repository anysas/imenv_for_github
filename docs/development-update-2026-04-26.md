# Algorithmic Gallery v2 — Corruption Iteration

Conceptual pivot: from passive observation (gaze) to active creative agency that the assistant gradually corrupts. New 90-second sandbox loop where an AI "assistant" mirrors then overrides the player's prop placement style.

This update logs the v1→v2 refactor — done as the autonomous Phase 0–D execution requested.

## What was built

### Curated prop library
- `StreamingAssets/curated-props.json` — 833 daily-life props filtered from the 24,048-model master metadata. Eight display groups: `office`, `lab`, `retail`, `domestic`, `furniture`, `item`, `tech`, `workshop`. All ≤3m in any axis, ≥20 polys, file-on-disk verified.
- Replaces the path-keyword allow/deny system in `GalleryManager` with a single explicit manifest.

### Core gameplay loop (V2 — `AlgorithmicGallery.Corruption` namespace)
- `CuratedPropManifest.cs` — Loads manifest. Random / weighted-by-tags / drifted-from-tags queries.
- `StyleProfile.cs` — Records every placement (player + assistant). Tracks group counts, tag counts, density grid, cadence. Exposes dominant-N queries for the assistant.
- `HotbarController.cs` — 5 prop slots. Number-key selection. Reroll on consume. Assistant override probability gates how often rerolls bias toward the assistant's drift.
- `HotbarUI.cs` — Runtime-built UGUI canvas: 5 slot panels with thumbnail icons, key labels, display names, active-slot highlight. No prefab needed.
- `PropPlacer.cs` — Camera raycast → cube ghost preview → spawn on left click. Anti-overlap nudges placement by up to 6 spiral attempts. Used by both player and assistant.
- `AssistantSystem.cs` — 90-second arc with `_influenceCurve`. Three phases (Helping/Suggesting/Overriding) gate intervals, burst sizes, and prop-pick strategy. Drives PSX glitch intensity through `PSXRendererFeature.SetGlitchIntensity`.
- `SandboxManager.cs` — Top-level orchestrator. Loads manifest, owns the StyleProfile, runs the session timer (90s + 30s grace), fires `OnSandboxEntered` / `OnSessionComplete`.

### Auto-bootstrap (zero-wire scenes)
- `SandboxBootstrap.cs` — One-component entry point. Drop on an empty GameObject; press play.
- `SandboxManager` auto-creates: floor (20×20 plane), PropPlacer + SculptureSpawner, HotbarController + HotbarUI, AssistantSystem, RuntimeThumbnailCapture, EndCard, AssistantDebugUI, directional Sun, and a SimplePlayerRig if no MainCamera exists.

### Player & camera
- `SimplePlayerRig.cs` — CharacterController + WASD + mouse look + run modifier + cursor lock. Builds Camera child if absent. Tab to release cursor for debug.

### Polish & infra
- `PropPool.cs` — `PrewarmAsync(manifest)` heats up texture/mesh caches by loading N GLBs into a hidden far-below-world container.
- `RuntimeThumbnailCapture.cs` — Singleton with hidden camera + 96×96 RenderTexture. Renders each prop once, caches by ID, returns Sprite. Hooked into HotbarUI for slot icons.
- `HallwayTrigger.cs` — `OnTriggerEnter` calls `SandboxManager.BeginSandbox`. Tag-based filter with SimplePlayerRig fallback detection.
- `EndCard.cs` — Auto-built canvas. On `OnSessionComplete`, fades in a reflection: "It built you. You placed N objects. It learned that you preferred X. It saw you as Y. It then filled your space with what it thought you wanted."
- `AssistantDebugUI.cs` — F1 IMGUI overlay. Auto-resolves references in Start. Shows session time, influence, phase, top groups, top tags, hotbar contents.

### PSX visual pass
- `PSXRendererFeature.cs` + `PSXPass.cs` + `Shaders/PSXPost.shader` — URP renderer feature. Bayer 4×4 ordered dithering, color bit quantization (default 5 bits per channel), integer pixel scale, scan-line glitch. Glitch intensity is bound to `AssistantSystem.Influence` so the corruption is visible.

## What was deliberately *not* done

- **No edits to `SampleScene.unity`** — scene authoring is destructive and best handled in the editor. The bootstrap means the scene-graph contract is "drop one GameObject," so there's nothing to wire by hand.
- **No legacy script deletion** — V1 scripts (`GazeManager`, `RecommendationEngine` family, `GalleryManager`, `RoomLoopManager`, etc.) remain in `Assets/scripts/` for reference. They're documented as deprecated in `SCRIPTS_MANIFEST.txt` but compile cleanly.
- **No prefab generation** — would have required Editor scripting or risky binary YAML edits. UI / floor / player are all built at runtime instead.
- **No Hallway prefab** — gallery hallway grayboxing is Alyssa's track per the original plan.

## Files added (this session)

```
Assets/StreamingAssets/curated-props.json     833 props, 8 groups
Assets/scripts/CuratedPropManifest.cs
Assets/scripts/StyleProfile.cs
Assets/scripts/HotbarController.cs
Assets/scripts/HotbarUI.cs
Assets/scripts/PropPlacer.cs
Assets/scripts/PropPool.cs
Assets/scripts/RuntimeThumbnailCapture.cs
Assets/scripts/AssistantSystem.cs
Assets/scripts/AssistantDebugUI.cs
Assets/scripts/SandboxManager.cs
Assets/scripts/SandboxBootstrap.cs
Assets/scripts/SimplePlayerRig.cs
Assets/scripts/HallwayTrigger.cs
Assets/scripts/EndCard.cs
Assets/scripts/PSXRendererFeature.cs
Assets/scripts/PSXPass.cs
Assets/Shaders/PSXPost.shader
docs/setup-v2.md
docs/development-update-2026-04-26.md         (this file)
```

Updated:
```
Assets/scripts/SCRIPTS_MANIFEST.txt           split into V2 (active) + V1 (legacy reference)
```

## How to verify

1. Open the project in Unity.
2. **File → New Scene** (Empty).
3. Create empty GameObject, add `SandboxBootstrap` component.
4. Press Play.

Expected behaviour:
- Floor + sun appear.
- 5-slot hotbar at bottom of screen, each with a daily-life prop name.
- WASD to move, mouse look, click to place.
- After ~30s, hotbar starts getting "wrong" picks; assistant begins placing autonomously.
- After ~60s, assistant aggressively fills the space; PSX glitch intensifies.
- After 120s total, end card fades in.

For the PSX look: open the URP renderer asset and add `PSXRendererFeature` to its renderer features list (one-time editor action).

## Phase E — polish + bug-hardening (autonomous follow-up)

Added without user input after the initial Phase 0–D pass, to push the project as far as it could go before requiring an in-editor playtest.

### New systems
- `SessionExporter.cs` — Subscribes to `SandboxManager.OnSessionComplete`. Writes a JSON snapshot to `Application.persistentDataPath/sessions/session_<unix>.json` (timestamp, duration, final influence, total placements, group counts, tag counts). Replaces the "profile JSON export" item from the Next-priorities list.
- `PropBudget.cs` — Singleton cap (default 150) on simultaneously placed props. When exceeded, destroys the oldest assistant-placed prop first; falls back to oldest player-placed if assistant queue is empty. Prevents 90-second sessions from grinding the frame rate as the assistant accelerates.
- `AudioEscalation.cs` — `[RequireComponent(AudioSource)]`. Drives volume and pitch from `AssistantSystem.Influence` via two AnimationCurves; plays a one-shot SFX on each phase transition. Drop on any AudioSource with an ambient loop and it self-wires.
- `StyleProfileGizmo.cs` — `OnDrawGizmos` Scene-view density grid (10×10 over 20m) plus the last 20 placement markers as wire spheres. Editor-only debugging visual; no runtime cost in builds.
- `Editor/ThumbnailBaker.cs` — `Tools / Algorithmic Gallery / Bake Hotbar Thumbnails` menu item. Requires Play mode (uses `RuntimeThumbnailCapture`). Iterates the curated set serially and writes PNGs to `Assets/Resources/PropThumbnails/<id>.png`. Run-once-before-shipping; the runtime can then load via `Resources.Load<Sprite>` instead of capturing live.

### Bug fixes
- **PSX shader `_Time` collision** — Unity's built-in `_Time` uniform shadowed the shader's intended time uniform. Renamed to `_PSXTime` in [PSXPost.shader](Assets/Shaders/PSXPost.shader) and matching `Shader.PropertyToID` in [PSXPass.cs](Assets/scripts/PSXPass.cs).
- **PSX shader broken `Vert` wrapper** — Removed a duplicate `Varyings Vert(Attributes input)` that recursed on itself. Pass now relies on `Blit.hlsl`'s built-in vertex shader.
- **HotbarController null manifest crash** — Added early-out in `Update` when `_manifest == null` (could happen if manifest fails to load).
- **PropPlacer null `Camera.main` / cursor lock mismatch** — Raycast now detects cursor lock state and aims from screen center when locked, cursor position when not. Added null guards on `Camera.main` and `EventSystem.current`.
- **PropPlacer didn't notify SandboxManager** — Player placements weren't triggering reactive assistant bursts. Added `_sandbox?.NotifyPlayerPlaced(worldPosition)` after successful player spawn.
- **AssistantDebugUI references unset when added at runtime** — Auto-resolves all references via `FindFirstObjectByType` in `Start`.
- **PropPlacer `_sandboxRoot` null when auto-bootstrapped** — `Initialize` now falls back to `sandbox.SandboxFloor` if the inspector reference is empty.

### SandboxManager wiring updates
- Auto-bootstraps `PropBudget`, `SessionExporter`, and `EndCard` GameObjects in addition to the prior set.
- `PropPlacer.PlaceAt` now registers spawned props with `PropBudget.Instance` (with `isPlayerPlaced` flag) so eviction prefers assistant placements.

### Files added
```
Assets/scripts/SessionExporter.cs
Assets/scripts/PropBudget.cs
Assets/scripts/AudioEscalation.cs
Assets/scripts/StyleProfileGizmo.cs
Assets/scripts/Editor/ThumbnailBaker.cs
```

### Deliberately *not* done in Phase E
- **No `.asmdef`** — would force the V1 scripts (still in `Assets/scripts/`) into a separate assembly and break their cross-references. Defer until V1 is removed.
- **No scene file edits** — same reasoning as Phase 0–D; the bootstrap covers it.
- **No Windows build** — requires the user's machine and a target scene committed to Build Settings.

## Next priorities (post-autonomous)

1. **Hallway scene** — Alyssa graybox with framed snapshots of past sessions.
2. **Audio pass** — assistant footsteps / ambient escalation as influence grows.
3. **Profile JSON export** — write StyleProfile to disk at session end for playtest analysis.
4. **Pre-rendered thumbnails** — replace runtime capture with baked PNGs in `Resources/` for faster startup.
5. **Multiple endings** — branch end card based on whether player placed > N or stopped engaging.
6. **VR port** — InputSystem swap; rest of the architecture is pose-agnostic.

## Known limitations

- The runtime thumbnail capture assumes nothing else uses culling layer 31. If you have other geometry on that layer, change `_captureCamera.cullingMask` and the layer assignment in `CaptureOne`.
- Anti-overlap is a simple distance check against sandbox-floor children; it doesn't account for prop bounds (a 0.4m spacing between centers can still visually overlap large props). Acceptable for a 90s session; revisit if visual clutter is bad.
- `PSXPass` uses `Blitter.BlitCameraTexture` which requires URP 14+ (project is on 17.3 — fine).
- `SandboxManager.BootstrapMissingDependencies` reparents an existing MainCamera under a new PlayerRig — this can break scenes with complex camera hierarchies. Set `_autoBootstrap=false` if you have your own rig.
