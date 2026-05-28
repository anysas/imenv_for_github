# Algorithmic Gallery v2 - Reactive PSX VFX Plan (2026-04-28)

## Purpose
Use the new PSX/CRT post stack to reinforce the theme of lost creative agency by making the screen feel stable at first, then increasingly unstable during assistant interventions.

This plan focuses on **event-driven visual spikes** layered on top of your existing time-driven corruption arc.

## Current Shader/FX Analysis

### Active PSX pass (good baseline)
- `Assets/Shaders/PSXPost.shader` currently exposes:
  - `_ColorBits`
  - `_DitherStrength`
  - `_GlitchIntensity`
  - `_PixelScale`
  - `_PSXTime`
- It currently does:
  - ordered Bayer dithering
  - color quantization
  - horizontal row glitch offset
  - optional pixel stepping

### Runtime control path currently in use
- `AssistantSystem` drives `PSXRendererFeature.SetGlitchIntensity(Influence)` every frame.
- This gives a good base arc, but no discrete event spikes yet.

### Additional FX systems in project (conflict risk)
- `PhaseVolumeController` (Chromatic Aberration + Lens Distortion + Bloom, etc.) from V1 flow.
- CRT Volume component (`Crt`) + `CRTEffectController`.
- Older `GlitchRendererFeature`/`EnvironmentFXManager` path.

Important: if multiple systems simultaneously drive similar properties, spikes will feel inconsistent or get overwritten.

## Design Goals
1. Keep a smooth baseline corruption ramp (already present).
2. Add short, readable "panic pulses" at key narrative moments.
3. Avoid nausea/illegibility by using strict burst caps and cooldowns.
4. Ensure one clear runtime authority controls each effect channel.

## Visual Language (Narrative Mapping)
- **Helping phase**: mostly stable, occasional subtle scan jitter.
- **Suggesting phase**: detectable chromatic split and minor bend.
- **Overriding phase**: frequent brief distortion bursts + stronger quantization artifacts.
- **Assistant action moments**: sharp micro-spikes (100-500 ms) on top of baseline.

## Event -> Effect Plan

### 1) Sandbox entered
- Trigger: `SandboxManager.OnSandboxEntered`
- Effect:
  - short "signal lock" sweep (`glitch +0.12`, `chromatic +0.02`)
  - settle back to baseline in ~0.6s

### 2) Assistant activation (5th player placement)
- Trigger: `AssistantSystem.OnActivated`
- Effect:
  - stronger burst (`glitch +0.25`)
  - temporary lens distortion dip (e.g. `-0.05`)
  - short color-bit drop (e.g. 5 -> 4) for 0.3s, then restore

### 3) Every assistant placement
- Trigger: `PropPlacer.OnPropPlaced(false)`
- Effect:
  - micro spike:
    - `glitch +0.10` for 0.15-0.25s
    - tiny chromatic bump (`+0.01 to +0.02`)
  - include cooldown so bursts cannot stack every frame under heavy spawn load

### 4) Assistant phase changes (Helping -> Suggesting -> Overriding)
- Trigger: when `AssistantSystem.Phase` changes
- Effect:
  - phase transition pulse:
    - Suggesting entry: mild wave
    - Overriding entry: strongest pulse in session
  - optionally increase base CRT bend by one notch per phase

### 5) Session complete
- Trigger: `SandboxManager.OnSessionComplete`
- Effect:
  - 0.8-1.2s "collapse" burst
  - then hold a dirty low-fidelity look (or cut to end card as desired)

## Implementation Architecture

## Step A - Introduce a single runtime FX director
Add a new script:
- `Assets/scripts/SandboxReactiveVfxDirector.cs`

Responsibilities:
- subscribe to:
  - `OnSandboxEntered`
  - `OnSessionComplete`
  - `AssistantSystem.OnActivated`
  - assistant phase changes
  - `PropPlacer.OnPropPlaced`
- maintain:
  - baseline values (from time/influence)
  - transient impulses (event bursts)
  - cooldowns
- blend output each frame:
  - `final = baseline + impulse`, then clamp

## Step B - Separate "baseline" from "burst" control in PSX feature
Current API sets one value directly (`SetGlitchIntensity`), which makes burst layering awkward.

Refactor to:
- `SetBaseGlitchIntensity(float)`
- `AddGlitchImpulse(float amplitude, float duration)`
- pass internally computes effective glitch each frame with decay

This allows AssistantSystem to keep baseline while director injects bursts.

## Step C - Optional CRT + volume integration
If using `Crt` and/or URP volume overrides in sandbox:
- drive only a small subset reactively:
  - chromatic aberration
  - lens distortion
  - screen bend
- do not spike all channels at once.

## Conflict Controls (Important)
1. If `CRTEffectController` is active, it may overwrite runtime changes every Update.
   - Option: disable it in play mode for sandbox scene, or extend it to accept runtime multipliers.
2. Avoid running both old `GlitchRendererFeature` and new `PSXRendererFeature` as primary glitch source in sandbox.
3. Keep one owner for each property family:
   - PSX glitch: PSX feature + director
   - Volume chromatic/lens: director only (or phase controller only, not both)

## Suggested Safe Ranges
- Base glitch: `0.00 -> 0.35` across session
- Burst glitch additive: `+0.08 -> +0.30` (hard clamp final <= `0.65`)
- Chromatic aberration:
  - base `0.00 -> 0.12`
  - burst `+0.01 -> +0.05` (clamp <= `0.20`)
- Lens distortion intensity:
  - base `0 -> -0.06`
  - burst down to `-0.12` max
- Screen bend (CRT):
  - subtle stepped change per phase, no aggressive oscillation

## Timing/Cooldown Rules
- Assistant placement burst cooldown: `0.25-0.40s`
- Phase-change burst cooldown: `1.0s`
- Overlapping bursts should add partially, not fully (e.g. 50% stacking)

## Playtest Checklist
1. Early game still readable and comfortable.
2. 5th placement activation is clearly "felt" visually.
3. Each assistant placement gives a noticeable but brief hit.
4. Overriding phase feels unstable, not unplayable.
5. No flicker-induced illegibility on hotbar/terminal text.
6. No property tug-of-war from multiple effect controllers.

## Rollout Plan (Low Risk)
1. Implement PSX burst layering only (glitch channel).
2. Playtest and tune burst amplitude/cooldown.
3. Add chromatic/lens reactive spikes.
4. Add optional CRT bend phase stepping.
5. Lock values for final showcase build.

## Recommendation
Start with **glitch-only event spikes** for one pass. Once timing feels right, add very light chromatic/lens response. This keeps the effect readable and avoids over-stacking multiple stylized post channels too early.
