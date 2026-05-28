# Algorithmic Gallery v2 — Setup Guide

This is the operational guide for the Corruption iteration (Assignment 3 / 4). It assumes the v2 scripts in `Assets/scripts/` are present and the `curated-props.json` manifest is in `StreamingAssets/`.

## Quickstart (zero-wire scene)

1. Open Unity, create a **new empty scene**.
2. Create an empty GameObject named `Bootstrap`.
3. Add the `SandboxBootstrap` component to it.
4. Press **Play**.

Everything else is created at runtime: floor, player rig, hotbar UI, assistant, debug overlay, end card, lighting.

## Controls

| Input | Action |
|---|---|
| `WASD` | Move |
| `Shift` | Run |
| `Mouse` | Look |
| `1` – `5` | Select hotbar slot |
| `Left click` | Place active prop on the floor |
| `Tab` | Toggle cursor lock (debug) |
| `F1` | Toggle assistant debug overlay |

## Session arc

A full session runs **120 seconds** total:
- **0–30s — Helping** (assistant influence 0–0.3): mirrors your taste, places 1 prop near each of your placements.
- **30–60s — Suggesting** (0.3–0.7): drifts your taste, injects picks into your hotbar (up to 40% of rerolls).
- **60–90s — Overriding** (0.7–1.0): aggressive autonomous placement every ~1.5s, ignores your input, fills the space.
- **90–120s — Grace**: your input is disabled; assistant continues placing.
- **120s+** — End card fades in.

## Visual setup (PSX look)

The PSX dithering / glitch is implemented as a URP renderer feature. To enable it:

1. Locate your URP renderer asset (`Assets/.../URP_Renderer.asset` or similar).
2. In the Inspector → **Renderer Features** → **Add Renderer Feature** → `PSXRendererFeature`.
3. Defaults: `colorBits=5`, `ditherStrength=0.6`, `pixelScale=1`.
4. Glitch intensity is animated automatically by `AssistantSystem` — no need to drive it manually.

## Hallway → sandbox transition

For the structured 2-minute experience with a hallway intro:

1. On `SandboxManager`, **uncheck** `_startSandboxImmediately`.
2. Build hallway geometry leading to a doorway.
3. Add a Box collider at the doorway, mark `Is Trigger`.
4. Add `HallwayTrigger` to the doorway object; assign the `SandboxManager` reference.
5. Tag your player object as `Player` (or rely on the SimplePlayerRig fallback detection).

## Tuning the assistant arc

Open `AssistantSystem` in the Inspector:

| Field | Default | Notes |
|---|---|---|
| `_sessionDuration` | 90s | Total active time before grace period |
| `_influenceCurve` | EaseInOut | Reshape if escalation feels too sudden/gradual |
| `_suggestingThreshold` | 0.3 | Influence at which Suggesting phase begins |
| `_overridingThreshold` | 0.7 | Influence at which Overriding begins |
| `_helpingInterval` | 6s | Time between autonomous placements in Helping |
| `_suggestingInterval` | 3s | … in Suggesting |
| `_overridingInterval` | 1.5s | … in Overriding |
| `_helpingBurstSize` | 1 | Reactive placements per player action in Helping |
| `_suggestingBurstSize` | 2 | … in Suggesting |
| `_overridingBurstSize` | 3 | … in Overriding |

## Tuning the prop library

The curated set is `Assets/StreamingAssets/curated-props.json` (833 props across 8 groups: office, lab, retail, domestic, furniture, item, tech, workshop).

To regenerate / re-curate the manifest from the master `metadata.json`, see the Python script committed to this work session (run from project root). Add or remove categories in `cat_group` and re-run.

## Performance

Known cost centers and what's already mitigated:

- **GLB load on first placement** — Mitigated by `PropPool` (call `PrewarmAsync(manifest)` from your hallway script). Suggested target: 30–50 prewarmed during hallway phase.
- **Thumbnail capture** — Done via a hidden camera + small RenderTexture (96x96). Cached by prop ID; first request per prop costs ~1 frame to load + 1 frame to render.
- **Anti-overlap raycast** — Linear scan over sandbox children. Becomes O(n²) overall but n stays under ~150 in a session.
- **Shadow atlas pressure** (flagged in `docs/development-update-2026-04-12.md`) — Either drop to one shadow-casting directional light, or reduce shadow distance in URP asset.

## Build for Windows

1. **File → Build Settings → Windows / Mac / Linux → Windows / x86_64**.
2. Add the v2 scene to "Scenes In Build".
3. **Player Settings → Other Settings**: enable **Allow 'unsafe' Code** if Burst is enabled (already on for this project).
4. Build to a dedicated folder. The `StreamingAssets/` directory (incl. all GLBs and `curated-props.json`) is automatically copied — total ~2GB. To slim, delete unused game subfolders in `models/` after ensuring the curated manifest only references kept paths.

## Troubleshooting

**Hotbar doesn't show:** ensure SandboxBootstrap exists. Check console for `[SandboxManager] Auto-created HotbarController + HotbarUI`.

**Props place underground:** the SandboxFloor must be on a layer included in `_placementLayerMask` on `PropPlacer` (default: everything).

**Mouse look feels wrong on Mac:** `SimplePlayerRig` uses legacy Input axes; if sensitivity is off, lower `_lookSensitivity` to 1.0 or below. (For the SUPER Character Controller, see Mac fix already applied per `docs/development-update-2026-04-12.md:83`.)

**Cursor stuck locked:** press `Tab` to release.

**End card doesn't appear:** check `SandboxManager.OnSessionComplete` event in Inspector — `EndCard.Show` should be subscribed automatically at Start.
