# Sandbox UX Fixes - Implementation Plan (2026-04-28)

## Goal
Implement the five sandbox UX fixes in `Trial1.unity` with low-risk, scoped script changes while preserving existing V1 compatibility and current project architecture.

## Scope
Target files:
- `Assets/scripts/SculptureSpawner.cs`
- `Assets/scripts/PropPlacer.cs`
- `Assets/scripts/AssistantSystem.cs`
- `Assets/scripts/HotbarUI.cs`

Out of scope:
- `StyleProfile.cs`, `SandboxManager.cs`, `HotbarController.cs`, `RuntimeThumbnailCapture.cs`, `PSXPass.cs`, curated JSON dataset.

## Implementation Order
1. Change 1: real-world prop dimensions (scaling behavior)
2. Change 2: assistant activation threshold
3. Change 3: hide hotbar until sandbox entry
4. Change 4: control hints in bottom-right
5. Change 5: actual model ghost preview

Reasoning:
- Change 1 enables correct scale path for Change 5 ghost loading.
- Changes 2-4 are independent and low-risk.
- Change 5 has async/race complexity, so it lands last.

---

## Change 1 - Use Real-World Prop Dimensions

### Problem
`SculptureSpawner.LoadModel` currently normalizes all models to a fixed dimension, removing meaningful size variance and inflating small props.

### Plan
- Add optional `normalizeScale` argument to `LoadModel` with default `true`.
- Apply normalization only when both:
  - class-level normalization setting is enabled
  - `normalizeScale == true` for the call
- In `PropPlacer.SpawnProp`, call `LoadModel(..., normalizeScale: false)`.
- Keep thumbnail capture behavior unchanged (defaults to normalized).

### Expected Outcome
Placed props retain natural GLB scale, restoring realistic cup/chair/fridge relative sizing.

### Risk / Mitigation
- Risk: some objects may feel too large.
- Mitigation: optional future clamp field (`_maxAllowedDimension`) if playtests require it.

---

## Change 2 - Gate Assistant Until 5 Player Placements

### Problem
Assistant intervention begins immediately, flooding the room before player intent is established.

### Plan
- Add serialized threshold field:
  - `_minPlayerPlacementsBeforeActive = 5`
- Gate in three places:
  - Reactive burst logic in `OnPlayerPlaced`
  - Autonomous spawn timing branch in `Update`
  - `UpdateHotbarInfluence` call
- Keep influence curve and visual ramp time-based and always active.

### Expected Outcome
Player gets a setup window before the assistant starts to manipulate outcomes.

### Trade-Off
If user places slowly, assistant has less active time in the 90s session. This currently aligns with the intended theme.

---

## Change 3 - Hide Hotbar Until Sandbox Entry

### Problem
UI appears from scene start, including hallway traversal.

### Plan
- Add `CanvasGroup` to hotbar root after canvas build.
- Initialize hidden state (`alpha = 0`, `blocksRaycasts = false`).
- In `Start`, subscribe to `SandboxManager.OnSandboxEntered`.
- Reveal UI when event fires.
- If `SandboxManager.SandboxActive` is already true on startup, reveal immediately.

### Expected Outcome
UI appears only at interaction context, reducing pre-sandbox visual noise.

### Design Choice
Use `CanvasGroup` instead of disabling root object so thumbnail updates can continue while hidden.

---

## Change 4 - Add Control Hint Overlay

### Problem
No immediate interaction affordance for placement and deletion controls.

### Plan
- Extend `HotbarUI.BuildCanvas` to create bottom-right hint text:
  - `"Right click - place\nLeft click - destroy"`
- Use same built-in font path and subdued text alpha.
- Parent hint under the same `CanvasGroup` used in Change 3.

### Expected Outcome
New players can start interacting without verbal instruction.

---

## Change 5 - Replace Cube Ghost with Actual Model Ghost

### Problem
Current preview is a generic cube and does not communicate object identity or footprint.

### Plan
- Reuse serialized `_ghostMaterial`; provide fallback translucent material at runtime if unset.
- Add state fields in `PropPlacer`:
  - `_ghostInstance`
  - `_ghostProp`
  - `_ghostLoadingProp`
- Add `LoadGhostAsync(PropEntry prop)`:
  - load model with `normalizeScale: false`
  - race-check against current active prop after load
  - disable colliders on ghost
  - swap renderer materials with ghost material
  - destroy prior ghost instance before assign
- Rewrite `UpdateGhostPreview`:
  - trigger async load on active-prop change
  - if no ghost loaded yet, skip display
  - if floor hit: position and show ghost
  - if no hit: hide ghost

### Expected Outcome
Ghost reflects actual selected prop and its real-world scale.

### Risk / Mitigation
- Risk: rapid slot scroll causes overlapping async requests.
- Mitigation: active-prop race checks + single-instance replacement strategy.

---

## Verification Checklist (Play Mode in `Assets/Scenes/Trial1.unity`)
- Hallway traversal shows no hotbar/hints before sandbox trigger.
- Enter sandbox: hotbar + hint text become visible.
- Cursor over floor: translucent active-prop ghost appears.
- Slot switch: ghost updates to new prop within acceptable latency.
- Right click: placed object size reflects natural GLB scale.
- Left click: object removal still works.
- Placements 1-4: assistant remains inactive.
- Placement 5: assistant reactive/autonomous behavior begins.
- End of 90s flow still reaches end card with expected logs.

## Rollback Controls
- Scaling rollback: set `normalizeScale: true` in `PropPlacer` call path.
- Assistant rollback: set `_minPlayerPlacementsBeforeActive = 0`.
- UI rollback: disable CanvasGroup reveal path.
- Hint rollback: remove hint builder method call.
- Ghost rollback: fallback to old cube preview if model ghost load fails repeatedly.

## Execution Notes
- Keep edits local and method-scoped.
- Do not alter V1 usage defaults.
- Validate all five checks in one Play session, then run one stress pass with rapid slot scrolling.
