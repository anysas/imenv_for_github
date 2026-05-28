# Analysis Terminal Screen Plan (2026-04-28)

## Goal
Ship a diegetic wall-mounted "analysis terminal" in the sandbox that clearly communicates:
- what the system thinks the player's style is
- how confident it is
- how close the assistant is to overriding intent

This plan assumes your current runtime script exists:
- `Assets/scripts/ProfileTerminalDisplay.cs`

## Current Capability (Already Implemented)
- `ProfileTerminalDisplay` can:
  - auto-build fallback world-space canvas/text if references are unassigned
  - subscribe to `SandboxManager` events
  - show boot sequence on sandbox entry
  - pull live metrics from `StyleProfile` + `AssistantSystem`
  - freeze output on `OnSessionComplete`

## Phase 1 - Scene Integration (Fast)

1. In `Assets/Scenes/Trial1.unity`, select your wall screen object (or create one).
2. Add `ProfileTerminalDisplay` component.
3. Leave UI text refs empty for first pass (fallback runtime UI builds automatically).
4. Position/scale wall object so text is readable from normal standing distance.
5. Enter play mode and verify:
   - hidden before sandbox entry
   - boot sequence on entry
   - live updates during placements
   - final freeze on session complete

## Phase 2 - Production UI Wiring (Recommended)

Replace fallback UI with manually authored world-space canvas:

1. Create child world-space Canvas under wall screen root.
2. Build panels/texts for:
   - header
   - status block
   - behavior block
   - preference block
   - risk block
   - footer
3. Assign these text components to the corresponding fields on `ProfileTerminalDisplay`.
4. Disable fallback build:
   - `_buildRuntimeCanvasIfMissing = false`

Benefits:
- art-directed typography and spacing
- cleaner integration with Alyssa's wall design
- easier animation/shader styling

## Phase 3 - Styling for Vibe

Apply light terminal aesthetics without hurting readability:
- color palette: green/cyan base, amber warning accents in risk/footer
- subtle scanline/opacity noise via material or overlay
- small emissive bloom around text
- occasional jitter/flicker only on event bursts (assistant placements/activation)

Keep text legible:
- avoid continuous violent shake
- keep contrast high
- avoid heavy chromatic split on terminal itself

## Data Blocks to Show (Final Format)

### Header
- `AG_SYS PROFILE CONSOLE // SUBJECT: VISITOR_01`

### Status
- session time
- assistant phase
- influence
- confidence
- runtime state

### Behavior
- player placement count
- assistant placement count
- average cadence

### Preferences
- top groups
- top tags

### Risk
- agency retention bar
- autonomy override bar

### Footer
- phase-specific narrative status line

## Event Reactivity (Optional but Strong)

Connect terminal visuals to assistant events:
- sandbox entry: boot sequence
- assistant activation: warning tint pulse
- assistant placement: brief line flash
- phase changes: title/status color shift
- session complete: lock and dim

Implementation option:
- add a tiny `TerminalPulseController` that listens to:
  - `AssistantSystem.OnActivated`
  - `PropPlacer.OnPropPlaced(false)`
  - `SandboxManager.OnSessionComplete`

## Performance Notes
- refresh interval around `0.20 - 0.35s` is enough (no per-frame string rebuild required)
- avoid allocating many large strings each frame
- keep one canvas and reuse text components

## Validation Checklist
- terminal appears only in sandbox phase
- all metrics match debug overlay values
- no null-reference spam when references are missing
- no font errors in console
- text readable at intended player distance
- freeze behavior works on session complete

## Suggested Rollout
1. Ship Phase 1 quickly for function check.
2. Move to Phase 2 once placement on wall is locked.
3. Add Phase 3 polish after PSX reactive spikes feel right.
