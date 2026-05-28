# Development Plan — 2026-04-26 onwards
## Algorithmic Gallery v2 → A3+A4 ship

All systems code is in place ([project-brief-updated-2026-04-26.md](project-brief-updated-2026-04-26.md)). What's left is mostly **scene work** + small code adds + playtesting. Plan is staged so each phase ends in a playable build.

Sequence is deliberate: F unblocks G, G unblocks playtesting, playtesting unblocks tuning, tuning unblocks the Windows build. Don't skip ahead.

---

## Phase F — Bring the scene up (today, ~30 min) **← do first**

**Goal**: SampleScene runs end-to-end with the new system. Confirms nothing is broken before adding more.

### F.1 Wire SandboxBootstrap into SampleScene
1. Open `Assets/Scenes/SampleScene.unity` in Unity.
2. `GameObject → Create Empty`, name it `_Bootstrap`.
3. Add component → `SandboxBootstrap` (under `AlgorithmicGallery.Corruption`).
4. Save scene.
5. Press Play. Expected:
   - 20×20 floor plane appears under origin.
   - Player rig (FPS camera) at `(0, 0, -3)`, cursor locked.
   - Hotbar at bottom of screen with 5 slots, each with a prop name.
   - WASD + mouse to move, click to place, 1–5 to select, F1 for debug.

### F.2 Verify the assistant arc
1. With Play running, place 3–4 props.
2. After ~25 s the assistant should start placing reactively (you'll see a prop spawn near your last placement).
3. After ~60 s, props should appear faster, drifting away from your tag profile.
4. After 90 s, hotbar fills get aggressive.
5. After 120 s total, end card fades in.

### F.3 Register PSX renderer feature
1. Select `Assets/Settings/PC_Renderer.asset` in the Project window.
2. Inspector → Renderer Features → `+` → `PSX Renderer Feature`.
3. Press Play, verify dithering + colour quantization is visible.
4. As assistant influence ramps, glitch (scan-line distortion) should intensify.

### F.4 Bake hotbar thumbnails (optional but recommended)
- Enter Play mode (in an empty scene or SampleScene before sandbox starts).
- Menu: `Tools / Algorithmic Gallery / Bake Hotbar Thumbnails`.
- Wait ~10 min — writes 833 PNGs to `Assets/Resources/PropThumbnails/<id>.png`.
- *(If skipped: hotbar uses runtime capture instead — slightly slower first-frame but works.)*

### F.5 Acceptance criteria
- [ ] Game runs from press-play without manual wiring beyond F.1.
- [ ] All three assistant phases are observable.
- [ ] PSX shader visibly active.
- [ ] End card appears at session end.

**Blocker**: if any of the above fails, debug before moving on. The whole rest of the plan depends on this loop being clean.

---

## Phase G — Spatial flow: hallway + sandbox layout (this week) **← Alyssa lead**

**Goal**: Replace the empty default scene with the gallery experience the brief calls for. Visitor walks through hallway, sees past creations, crosses threshold, enters sandbox.

### G.1 New scene: `HallwaySandbox.unity`
1. `File → New Scene → Empty (URP)`.
2. Save as `Assets/Scenes/HallwaySandbox.unity`.
3. Add to Build Settings (`File → Build Settings → Add Open Scenes`).
4. Drop `_Bootstrap` GameObject (`SandboxBootstrap` component) — but set `_startSandboxImmediately = false` on the auto-created `SandboxManager` (or expose this on the bootstrap script).

### G.2 Hallway graybox *(Alyssa)*
- Long corridor (~12 m) with low ceiling, ProBuilder walls.
- 3–4 framed pedestals along the walls. Each pedestal has:
  - A wall-mounted picture frame (~1 m wide).
  - A nameplate beneath ("Visitor 1", "Visitor 2", etc.)
- Lighting: dimmer, more contrast than the sandbox. Let the sandbox feel brighter/more open by comparison.
- End of hallway: a doorway/threshold collider that triggers `SandboxManager.BeginSandbox()`.

### G.3 Sandbox room *(Alyssa)*
- Single large room (20×20 m to match `SandboxFloor` default), bounded walls.
- Boundary visual: a subtle floor border, not a hard wall, so it feels open.
- Single point/area light from above, neutral.
- Place player spawn marker just inside the threshold from the hallway.

### G.4 Threshold trigger
1. Add a thin box collider spanning the doorway, mark `Is Trigger`.
2. Add `HallwayTrigger` component.
3. Reference the `SandboxManager` in the inspector (or let it auto-resolve).
4. `_oneShot = true` so it doesn't re-fire if the player walks back.

### G.5 New code: `HallwayGallery.cs` *(Ezra, ~1 hr)*
Reads past session screenshots from disk and displays them on the hallway picture frames as Quad meshes with a runtime-loaded texture.

```
Inputs: Texture2D[] frames; Transform[] frameAnchors;
Behavior: at Start, load N most recent screenshots from
  Application.persistentDataPath/screenshots/, apply to anchor renderers.
Fallback: if fewer than N exist, show a "no record" placeholder texture.
```

### G.6 Acceptance criteria
- [ ] Player spawns in hallway, can walk forward.
- [ ] Past creations are visible on frames (or placeholders).
- [ ] Crossing threshold starts the 90-s clock.
- [ ] Sandbox room is bounded; player can't fall off the floor.

---

## Phase H — Session capture + branching end (small code, ~2 hr) **← Ezra**

**Goal**: Close the loop so each session leaves a trace (for the hallway) and ends with the right beat.

### H.1 Session screenshot
New script `SessionScreenshot.cs`:
- Subscribes to `SandboxManager.OnSessionComplete`.
- Hides hotbar UI (set canvas alpha 0) for 1 frame.
- Calls `ScreenCapture.CaptureScreenshot(persistentDataPath/screenshots/session_<unix>.png)`.
- Restores hotbar.
- Auto-bootstrap-add in `SandboxManager`.

This feeds Phase G.5.

### H.2 Branching end card
Modify `EndCard.cs` to choose copy based on:
- **Resistor**: `playerPlacements < 5` → "You hesitated. It built around your absence."
- **Collaborator**: `playerPlacements ≥ 5` and `assistantPlacements ≤ playerPlacements * 2` → existing copy ("It built you...")
- **Overrun**: `assistantPlacements > playerPlacements * 2` → "It learned what you wanted faster than you did."

All three end with the same screenshot prompt: "Press SPACE to keep your image."

### H.3 Persist final screenshot to user-visible location
- After the screenshot saves, also copy to `~/Desktop/AlgorithmicGallery_<timestamp>.png` (or Windows equivalent).
- Display the path on the end card so the player sees where it went.

### H.4 Acceptance criteria
- [ ] PNG appears on disk after every play session.
- [ ] End card copy varies based on engagement.
- [ ] Path to screenshot shown on end card.

---

## Phase I — UX polish (this week, parallel-track) **← split**

These are small but each measurably improves the experience. Pick off as time allows.

### I.1 Diegetic onboarding *(Alyssa visual + Ezra script)*
Brief: "visitor is encouraged to build through diegetic spatial elements."
- Glowing floor circle at sandbox center on entry.
- Subtle pulse on the active hotbar slot until first placement.
- Floor circle + pulse fade out after first placement.
- Implementation: `OnboardingHints.cs` — listens for first `OnPlayerPlaced`, hides itself.

### I.2 Real ghost preview *(Ezra, ~1 hr)*
Replace cube placeholder in `PropPlacer.UpdateGhostPreview`:
- Use `RuntimeThumbnailCapture` (or a sibling helper) to spawn the actual GLB into a "ghost" hidden world location, copy its mesh into a translucent in-place preview.
- Cache one ghost per prop.
- Apply a translucent blue material so it reads as "preview, not placed yet."

### I.3 Hotbar UI polish *(Alyssa)*
- Replace built-in `LegacyRuntime.ttf` with a real font (e.g. Inter or PSX-style pixel font — TextMeshPro).
- Add a "rerolling..." spinner animation when a slot is consumed.
- Fade out unused slots during Overriding phase to reinforce loss-of-control.

### I.4 Audio escalation *(Ezra wires, Alyssa picks clips)*
- Find / record an ambient drone for the sandbox.
- Add `AudioSource` to SandboxFloor → assign clip → add `AudioEscalation` component.
- Optional: a "phase change" SFX clip. Plays once when crossing 0.3 / 0.7 thresholds.
- Curves already in the inspector — tune live during playtest.

### I.5 Cursor / input lockdown
- Confirm Tab unlocks cursor (already coded). Useful for debugging.
- Add `Escape` to reveal a one-button "Quit" UI in case the player wants out.
- Handle alt-tab gracefully (cursor stays released until the player clicks back in).

### I.6 Acceptance criteria
- [ ] First-time player understands they're meant to place props within 3 s of entering sandbox.
- [ ] Ghost preview shows the actual model (or scaled silhouette).
- [ ] Hotbar reads cleanly at 1080p and 1440p.
- [ ] Audio responds to the assistant's rise.

---

## Phase J — Playtest + tune (between I and K)

**Goal**: Find the actual right values for the assistant arc, then lock them.

### J.1 First playtest (one person, single playthrough)
- Record screen.
- Take notes: where did the player feel agency? Where did it shift to "annoying"? When did the corruption read as intentional vs broken?

### J.2 Tuning checklist
Inspector values on `AssistantSystem`:
- `_sessionDuration` — too long? Brief says 90 s, but if onboarding takes 15 s, drop to 75 s of actual session.
- `_helpingInterval` — should be slow enough that players notice the assistant exists but don't feel pressured.
- `_overridingInterval` + `_overridingBurstSize` — if the room fills with literal trash by 75 s, dial back. The brief wants "overriding," not "destruction."
- `_influenceCurve` — try a logistic / S-curve shape so the shift from helping → overriding feels like a tipping point, not a linear ramp.

`PropBudget`:
- `_maxPlacedProps = 150` may be too generous. If FPS drops, lower to 100.

`PSXRendererFeature`:
- `colorBits = 5` is the floor of "PSX-y." Try 4 for harsher.
- `pixelScale = 2` for chunky pixels. Test for readability.

### J.3 Prop curation pass
- Walk the 833-prop list (or a sample). Cut anything that doesn't read as "everyday object on a gallery floor."
- Likely cuts: `lab` group (glassware feels too specific), `workshop` (welding equipment), some `tech` outliers (server racks).
- Edit `curated-props.json` directly or rerun the Python filter with a stricter list.

### J.4 Display name pass
Many display names are auto-generated junk ("Binderbluelabel01A"). Consider:
- A bulk regex pass to clean obvious patterns (numbered duplicates, model variants).
- Or: hide display names entirely once the visual icon is enough.

### J.5 Acceptance criteria
- [ ] One full playthrough where the corruption arc reads as designed (not buggy, not boring).
- [ ] FPS stays above 50 throughout.
- [ ] At least 3 different sessions captured for the hallway frames.

---

## Phase K — Ship (final week)

### K.1 Windows build
1. `File → Build Settings → Switch Platform → Windows x64`.
2. Confirm `HallwaySandbox.unity` is the first scene listed.
3. `Build And Run`. Expect a long first build (asset import).
4. On the build PC: verify GLBs load (StreamingAssets path resolves), screenshots write to `%AppData%/.../persistentDataPath/screenshots/`, and the desktop screenshot mirror works.

### K.2 Install instructions for the show
- README.txt in the build folder: "Run AlgorithmicGallery.exe. Walk forward. Place objects. Stay 2 minutes."
- Test on a fresh Windows account (not your dev machine) to catch path issues.

### K.3 A4 deliverables
- Video recording: full playthrough at the target framerate.
- 5–10 still screenshots from different sessions (hallway frames → end-card → final state).
- Brief written reflection: how the system corrupted intent, what playtesters reported, what cuts you made.

### K.4 Acceptance criteria
- [ ] Build runs on a different machine without dev tools installed.
- [ ] Full session loop (hallway → sandbox → end card → screenshot) works.
- [ ] Performance acceptable on the show machine.

---

## Risk + contingency

| Risk | Mitigation |
|---|---|
| Hallway scene takes longer than expected | Smallest version still proves the idea — fall back to instant sandbox spawn (current behavior, F.1 only) |
| Screenshot looks bad (UI in shot, weird angle) | H.1 hides the UI; pick a stable camera angle. Worst case, capture mid-fade-to-black so it's painterly |
| Playtest reveals the arc is fundamentally not fun | The arc is supposed to feel uncomfortable — that's the point. Distinguish "intentionally uncomfortable" from "broken" with a co-tester |
| Windows build has GLB streaming bugs | Falls back to runtime errors visible in `Player.log`. Pre-emptively test build on Windows mid-Phase J, not day-of |
| Assistant fills the room with chaos players hate | Lower `_overridingBurstSize` and `_maxPlacedProps`. Also consider a "diminishing returns" curve where the assistant slows down once the room is dense |

---

## Suggested daily breakdown

| Day | Ezra | Alyssa |
|---|---|---|
| **1 (today)** | Phase F (scene bring-up), F.4 thumbnail bake | Phase G hallway graybox sketch on paper |
| **2** | H.1 + H.2 (screenshot + branching end) | G.2 hallway prototype in Unity |
| **3** | G.5 `HallwayGallery.cs`, I.2 ghost preview | G.3 sandbox bounds, G.4 threshold trigger |
| **4** | I.1 onboarding hints, I.4 audio wire-up | I.3 hotbar UI polish, prop name cleanup |
| **5** | First Windows build (test only) | First playtest pass |
| **6** | J.2 tuning | J.3 prop curation, J.4 display names |
| **7** | K.1 final build | K.3 video / screenshots / writeup |

Slip room: G and I can run in parallel; H is independent of both. Phase J is the synthesis point — don't tune until F+G+H land.

---

## What's intentionally out of scope

- VR port (architecture is ready; defer past A4)
- Multiplayer / shared sessions
- Full prop curation by hand (script-driven cuts only)
- Custom shader for vertex jitter (Phase 4 of original plan deferred — current PSX post-process is enough)
- Procedural hallway generation (single hand-built scene only — explicit lesson from `RoomLoopManager`)
