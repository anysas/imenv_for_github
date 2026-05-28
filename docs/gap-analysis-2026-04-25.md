# Gap Analysis — Plan vs Codebase (2026-04-25)

Audit of the v2 corruption iteration after the autonomous Phases 0–E push. Compares the original brief against what's on disk, lists what was completed without human input, and itemizes what still requires the user (in-editor work or judgment calls).

---

## ✅ Plan items that are complete in code

### Conceptual frame
- v1→v2 namespace split: V2 in `AlgorithmicGallery.Corruption`, V1 left intact in `AlgorithmicGallery` for reference.
- Sandbox loop: 90 s session + 30 s grace period, owned by [SandboxManager](Assets/scripts/SandboxManager.cs).
- Three-phase escalation (Helping → Suggesting → Overriding) gated by an AnimationCurve in [AssistantSystem](Assets/scripts/AssistantSystem.cs:14).

### Prop library
- 833 curated daily-life props in [curated-props.json](Assets/StreamingAssets/curated-props.json) across 8 groups (`office`, `lab`, `retail`, `domestic`, `furniture`, `item`, `tech`, `workshop`). All ≤3 m, ≥20 polys, file-on-disk verified.
- Manifest contract matches code ([CuratedPropManifest.cs:11](Assets/scripts/CuratedPropManifest.cs:11) PropEntry has every JSON field).

### Style tracking
- [StyleProfile](Assets/scripts/StyleProfile.cs): density grid + group counts + tag counts + cadence + placement history. Player vs assistant split tracked separately (`PlayerPlacementCount` / `AssistantPlacementCount`).
- Debug visualization: [StyleProfileGizmo](Assets/scripts/StyleProfileGizmo.cs) draws the grid in the Scene view.

### Hotbar
- [HotbarController](Assets/scripts/HotbarController.cs) — 5 slots, 1–5 keys, reroll on consume, assistant-override probability hook.
- [HotbarUI](Assets/scripts/HotbarUI.cs) — runtime UGUI canvas, no prefab needed, 96 px slots with thumbnail icons + key labels + display names.
- Thumbnails: [RuntimeThumbnailCapture](Assets/scripts/RuntimeThumbnailCapture.cs) renders each prop once and caches the Sprite. Bake-to-disk path: `Tools / Algorithmic Gallery / Bake Hotbar Thumbnails` ([ThumbnailBaker](Assets/scripts/Editor/ThumbnailBaker.cs)).

### Placement
- [PropPlacer](Assets/scripts/PropPlacer.cs): camera raycast, ghost preview, anti-overlap nudge, cursor-lock-aware. Used by both player and assistant via the `IsPlayerControlled` flag.
- [SculptureSpawner.LoadModel](Assets/scripts/SculptureSpawner.cs:57) signature matches every caller (PropPlacer, RuntimeThumbnailCapture, PropPool, ThumbnailBaker).

### Assistant behavior
- [AssistantSystem](Assets/scripts/AssistantSystem.cs) — phase-gated intervals/burst sizes/prop-pick strategies (mirror → drift → aggressive drift), reactive placement on player action, autonomous placement on timer.
- Drives [PSXRendererFeature.SetGlitchIntensity](Assets/scripts/PSXRendererFeature.cs:27) so the glitch grows with influence.

### PSX visual pass
- [PSXRendererFeature](Assets/scripts/PSXRendererFeature.cs) + [PSXPass](Assets/scripts/PSXPass.cs) + [PSXPost.shader](Assets/Shaders/PSXPost.shader). Bayer-4×4 dither, color-bit quantization, optional pixel scale, scan-line glitch. URP 14+ Blitter API.

### Player
- [SimplePlayerRig](Assets/scripts/SimplePlayerRig.cs) — CharacterController + WASD + mouse look + Tab to release cursor. Auto-builds Camera child + AudioListener + EventSystem.

### Session boundary + outputs
- Hallway entry: [HallwayTrigger](Assets/scripts/HallwayTrigger.cs) calls `BeginSandbox`. Lenient detection (tag OR SimplePlayerRig OR CharacterController parent).
- End card: [EndCard](Assets/scripts/EndCard.cs) — auto-built canvas, fades in on `OnSessionComplete`, shows player vs assistant counts + dominant groups/tags.
- JSON export: [SessionExporter](Assets/scripts/SessionExporter.cs) writes `Application.persistentDataPath/sessions/session_<unix>.json` with full breakdown.

### Performance
- [PropBudget](Assets/scripts/PropBudget.cs) caps active props at 150; evicts oldest assistant placement first.
- [PropPool](Assets/scripts/PropPool.cs) optional GLB prewarm.

### Audio
- [AudioEscalation](Assets/scripts/AudioEscalation.cs) drives volume/pitch from `Influence` and plays a one-shot SFX on phase changes.

### Debug
- [AssistantDebugUI](Assets/scripts/AssistantDebugUI.cs) — F1 IMGUI overlay showing session time, influence, phase, placement counts, top groups/tags, hotbar contents.

### Auto-bootstrap
- [SandboxBootstrap](Assets/scripts/SandboxBootstrap.cs) — drop on a GameObject; SandboxManager auto-creates floor, player rig, hotbar, AssistantSystem, EndCard, SessionExporter, PropBudget, RuntimeThumbnailCapture, AssistantDebugUI, directional light.

### Docs
- [setup-v2.md](docs/setup-v2.md), [development-update-2026-04-26.md](docs/development-update-2026-04-26.md), this gap analysis, and the V1/V2 split in [SCRIPTS_MANIFEST.txt](Assets/scripts/SCRIPTS_MANIFEST.txt).

---

## 🔧 Issues found in this audit and fixed in code

1. **`activeInputHandler` was set to "Input System (New) only"** ([ProjectSettings.asset:928](ProjectSettings/ProjectSettings.asset:928)). Every V2 script uses the legacy `Input.*` API (movement, mouse look, mouse buttons, hotkeys). Without this fix, the entire V2 control loop would be silently no-op. **Changed to `2` (Both)** so legacy Input API works alongside the New Input System package.
2. **EndCard misreported player count**. `PlacementCount` includes assistant placements; the on-screen text said "You placed N objects" using that total. Added `PlayerPlacementCount` / `AssistantPlacementCount` to StyleProfile, plumbed through PropPlacer / SessionExporter / EndCard / AssistantDebugUI.

---

## ❌ Gaps that require human / in-editor work

These cannot be done from script files alone — they need either Unity's editor UI, a target machine, or a creative judgment call.

### 1. Wire SandboxBootstrap into a scene  *(2 minutes, blocking playtest)*
- Open `Assets/Scenes/SampleScene.unity` (or File → New Scene).
- Create empty GameObject → add component `SandboxBootstrap`.
- Save scene. Press Play.
- The scene currently has only `Main Camera`, `Directional Light`, `Global Volume`. SandboxManager will reparent that camera into a `PlayerRig` automatically — destructive but tested for the default scene. If you want to keep the default camera untouched, delete it first.

### 2. Add `PSXRendererFeature` to the URP renderer  *(1 minute, optional but plan-spec)*
- Open `Assets/Settings/PC_Renderer.asset` in the inspector.
- Renderer Features → "Add Renderer Feature" → "PSX Renderer Feature".
- Tweak `colorBits` (default 5), `ditherStrength` (0.6), `pixelScale` (1.0).
- Without this the project still runs — it just looks like flat URP, no PSX dither/glitch.
- `Mobile_Renderer.asset` should get the same treatment if you intend to ship a Quest/Android build.

### 3. Build the hallway scene  *(Alyssa's track)*
- Plan calls for "hallway → sandbox" spatial flow with framed snapshots of past sessions on the walls.
- Code side is ready: [HallwayTrigger](Assets/scripts/HallwayTrigger.cs) just needs to be on a `Collider isTrigger=true` at the hallway/sandbox threshold, and `SandboxManager._startSandboxImmediately` must be set to `false` so it waits for the trigger.
- Snapshot frames: read JSONs from `Application.persistentDataPath/sessions/` at boot, render each as a small Canvas → texture → frame quad. Code for this is **not** written.

### 4. Bake hotbar thumbnails  *(1 minute, optional)*
- Tools → Algorithmic Gallery → Bake Hotbar Thumbnails (must be in Play mode).
- Iterates the 833 props serially — expect ~5–10 minutes the first time.
- Writes to `Assets/Resources/PropThumbnails/<id>.png`.
- Currently runtime capture is used; baking just saves first-frame cost on cold load. **Note:** the runtime path doesn't yet *prefer* baked PNGs over live capture — that wiring is the only "left to do" in code for this.

### 5. Wire AudioEscalation  *(2 minutes)*
- Add an empty GameObject + `AudioSource` with a long looping ambient clip (assigned in inspector).
- Add `AudioEscalation` to it.
- Optionally assign a phase-change SFX clip in the inspector slot.
- Not part of bootstrap because the audio assets aren't checked in.

### 6. Player tag on PlayerRig  *(0 minutes — only matters with HallwayTrigger)*
- HallwayTrigger has lenient detection (tag OR SimplePlayerRig OR CharacterController parent), so this is moot for the auto-bootstrapped rig. Only needed if the user replaces the rig with their own.

### 7. Build settings / target platform
- `EditorBuildSettings.asset` has only `SampleScene.unity`. Once you build the hallway as a separate scene, add it before SampleScene.
- Platform switch (PC, Mac & Linux Standalone → Windows x64) for the Windows build deliverable. Ensure `Player Settings → Other → Scripting Backend = IL2CPP` if you want a smaller binary; Mono is fine for development.
- Run `File → Build And Run` once on the target machine to verify the GLB load path resolves under StreamingAssets.

### 8. Tag setup for `Player`
- Default Unity tag exists. If HallwayTrigger is used, the PlayerRig needs the `Player` tag — but the lenient fallback in HallwayTrigger already handles SimplePlayerRig without tags. So practically: optional.

### 9. Scene lighting / skybox
- Bootstrap creates a directional light if none exists. Skybox is the URP default. For the brief's "ambient escalation" feel, the user should pick a flatter skybox or a dark color in `Window → Rendering → Lighting → Environment`.

### 10. Curated set sanity pass *(creative call)*
- 833 props is a lot. Plan's tone implied a tighter curated set ("daily-life real-world objects"). The current filter included `lab` glassware and some `workshop` heavy machinery that may not feel like a domestic gallery.
- Potential cut: drop `lab` and `workshop` groups → ~600 props. Edit in [curated-props.json](Assets/StreamingAssets/curated-props.json) by hand or rerun the original Python filter with a shorter `groups` list.

### 11. Visual reads & playtest tuning
- `_sessionDuration`, `_helpingInterval`, `_suggestingInterval`, `_overridingInterval`, `_burstSize` values in [AssistantSystem](Assets/scripts/AssistantSystem.cs) are first guesses. They want a real playtest.
- `_minPlacementSpacing` (PropPlacer, default 0.4 m) doesn't account for prop bounds — large items can still visually overlap.
- `_maxPlacedProps` (PropBudget, default 150) was chosen blind. Watch the GPU/CPU profiler in Override phase.

### 12. VR port (out of scope for this assignment, noted in plan)
- Architecture is pose-agnostic. SimplePlayerRig is the only Input.* consumer for movement. Replacing it with an XR rig + new ray source for PropPlacer is the only structural change.

### 13. Past-session snapshots in hallway
- Plan: hallway walls show framed thumbnails from prior playtests.
- Code: needs a `HallwayGallery` MonoBehaviour that reads `persistentDataPath/sessions/*.json`, picks N by recency, renders an aggregate or per-session card, and applies it as a texture. **Not written.**

### 14. Multiple endings
- Plan mentions branching end card based on engagement (e.g., player stopped placing). EndCard currently shows a single copy.
- Branch on `sp.PlayerPlacementCount < threshold` or `sp.AverageCadenceSeconds() > X` for a "you let it take over" variant.

---

## Suggested execution order for the human

1. (5 min) Reopen the project — Unity will regenerate `.meta` files for the V2 scripts (none of them have `.meta` yet on disk).
2. (2 min) Drop `SandboxBootstrap` on an empty GameObject in `SampleScene` and press Play. Verify hotbar appears, props place, end card fires.
3. (1 min) Add `PSXRendererFeature` to `PC_Renderer.asset`. Verify glitch ramps up over the 90 s session.
4. (5 min) Open `Tools / Algorithmic Gallery / Bake Hotbar Thumbnails` while in Play mode. Let it run. Hotbar icons load instantly on subsequent plays.
5. (15 min) Build for Windows from `File → Build Settings`. Verify the build runs and writes a session JSON.
6. (creative) Open the curated set, decide if `lab`/`workshop` props should be cut.
7. (creative, larger) Build the hallway scene with HallwayTrigger and (optionally) the past-session snapshot frames.
8. (creative) Playtest the timing values; adjust `_helpingInterval` / `_suggestingInterval` / `_overridingInterval` until the corruption pacing reads.

---

## Health summary

| Area | Status |
|---|---|
| Curated prop manifest | ✅ Complete |
| Style tracking | ✅ Complete |
| Hotbar logic + UI | ✅ Complete |
| Placement (player + assistant) | ✅ Complete |
| Assistant escalation | ✅ Complete |
| PSX shader pipeline | ✅ Code complete · ❌ Renderer feature not registered |
| Player rig + camera | ✅ Complete |
| Session boundary + JSON export | ✅ Complete |
| End card | ✅ Complete |
| Performance budget | ✅ Complete |
| Audio escalation | ✅ Code complete · ❌ Not wired in scene |
| Debug overlay | ✅ Complete |
| Auto-bootstrap | ✅ Complete |
| Sample scene wiring | ❌ User adds SandboxBootstrap |
| Hallway scene | ❌ Alyssa's track |
| Hallway snapshot frames | ❌ Not written |
| Branching endings | ❌ Not written |
| Windows build | ❌ User runs build |
| Playtest tuning | ❌ User playtests |
