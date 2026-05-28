# Immersive Environments A3+4 — Algorithmic Gallery v2
## Status update: 2026-04-26

---

## Concept (unchanged)

**The force is misinterpretation.** An "assistant" observes the visitor's building behaviour, constructs a profile of their creative intent, and uses it to "help" — until it overtakes. The assistant IS the gallery space: a system that should serve, that gradually colonises.

Core themes: loss of creative agency · AI as well-intentioned saboteur · platforms shaping user output · satisfaction that isn't yours.

Touchstones: Voima / Lucid Blocks, PSX aesthetic (Lethal Company, Mouthwashing), Katamari Damacy.

---

## What's built and working in code ✅

Everything below exists as runtime Unity scripts, compiles against the project's packages (URP 17.3, GLTFast 6.18, Newtonsoft.Json 3.2.2), and has been audited for cross-script consistency.

### Prop library
- **833 curated daily-life props** across 8 groups: `office`, `lab`, `retail`, `domestic`, `furniture`, `item`, `tech`, `workshop`. All ≤3 m, ≥20 polys, file-on-disk verified. Lives in `Assets/StreamingAssets/curated-props.json`.
- Weighted random selection: match player tags, drift from tags, full random.

### Hotbar
- 5 prop slots, keys 1–5 to select, slot rerolls after placement.
- Slots bias toward the player's established tag profile; assistant overrides increase as influence grows.
- Runtime-built UGUI — no prefab needed. Slot icons populated via a hidden capture camera (96 px per slot); can be baked to disk for faster loads.

### Player tracking (StyleProfile)
- Records every placement (player and assistant separately).
- Tracks group counts, tag frequencies, spatial density grid (10×10 over 20 m), placement cadence.
- Exposes dominant groups/tags for the assistant; cadence for escalation tuning.
- Debug overlay (F1) shows live profile: session time, influence, phase, placement breakdown, hotbar contents.

### 90-second assistant arc
Three explicit phases, driven by a single `influence` float (0 → 1 over 90 s), shaped by an AnimationCurve the designer can tune in the inspector:

| Phase | Influence | Interval | Behaviour |
|---|---|---|---|
| Helping | 0–0.3 | Every 6 s | Mirrors player's tags, 1 prop per player action |
| Suggesting | 0.3–0.7 | Every 3 s | Drifts toward adjacent tags, 2–3 props per action, starts injecting hotbar picks |
| Overriding | 0.7–1.0 | Every 1.5 s | Aggressively different, fills sparse zones, overrides hotbar 100% |

### PSX visual pass (code complete)
- URP Renderer Feature: Bayer 4×4 ordered dithering, 5-bit colour quantization, pixel scale, scan-line glitch.
- Glitch intensity is bound directly to `AssistantSystem.Influence` — the corruption is visible in the image.
- **Not yet registered in the renderer asset** — one-click setup needed (see below).

### Session outputs
- **End card**: fades in at session end, shows "You placed N / It placed M / It saw you as [tags]."
- **JSON export**: full session record written to `persistentDataPath/sessions/session_<unix>.json` — placement positions, groups, tags, timing, influence. Useful for A4 analysis/show.
- **Session screenshot**: *not yet implemented* (see below — it's on the short list).

### Hallway → sandbox flow (code side)
- `HallwayTrigger.cs`: place on a trigger collider at the threshold, wires to `SandboxManager.BeginSandbox()`.
- `SandboxManager._startSandboxImmediately = false` to wait for the trigger.

### Auto-bootstrap
Drop a single `SandboxBootstrap` component on any empty GameObject and press Play. It creates: floor, player rig, hotbar, assistant, end card, prop budget, thumbnail capture, debug UI, directional light. Nothing needs to be hand-wired in a fresh scene.

### Performance
- `PropBudget` caps active props at 150; evicts oldest assistant placements first.
- `PropPool` optionally prewarms 30 GLBs at hallway-start to prevent first-placement hitches.
- `AudioEscalation` drives volume/pitch from influence — code exists, needs an audio clip wired in scene.

---

## What's partially done / needs in-editor setup ⚠️

These are working in code but need one-time editor steps:

| Item | Time | Steps |
|---|---|---|
| **Scene wiring** | 2 min | Open `SampleScene`, create empty GameObject, add `SandboxBootstrap`, press Play |
| **PSX renderer feature** | 1 min | `Assets/Settings/PC_Renderer.asset` → Add Renderer Feature → PSX Renderer Feature |
| **Hotbar thumbnails baked** | ~10 min | Enter Play mode → `Tools / Algorithmic Gallery / Bake Hotbar Thumbnails` |
| **Audio escalation** | 2 min | Add `AudioSource` + ambient loop clip + `AudioEscalation` component to any GameObject |

---

## What's NOT done yet — left to build ❌

### High priority (needed for the experience to function at its full concept)

**Hallway scene** *(Alyssa's track)*
- Grayboxed room leading to the sandbox with 3–4 framed "previous creation" pedestals.
- The pedestals imply the assistant has done this before — key to the diegetic logic.
- Code side: `HallwayTrigger` ready to drop in. `SandboxManager._startSandboxImmediately` = false.
- Past-session frames: needs a small `HallwayGallery.cs` that reads session JSONs from disk and renders them as wall-mounted image cards. *Not yet written.*

**Session screenshot / photo**
- Brief says: "at the end takes a screenshot of the finished creation for player to keep."
- Not implemented. Could use `ScreenCapture.CaptureScreenshot(path)` on `OnSessionComplete`. Quick to add — 10 lines. Add during scene setup.

**Branching end card**
- Currently one version of the end copy.
- Branch on: did the player give up (cadence → 0 before 60 s)? Did the player place very little (< 5 props)? Did the player fight through to the end?
- Copy variants roughed out in the brief; code structure in `EndCard.cs` makes this easy to add.

### Medium priority (polish + A4 deliverable quality)

**Playtest timing tuning** *(needs a human in the scene)*
- Current phase intervals (6 s / 3 s / 1.5 s) and burst sizes (1 / 2 / 3) are first guesses. They will almost certainly feel wrong. Plan for a full playtest session and tune via the inspector.
- `_sessionDuration`, `_helpingInterval`, `_suggestingInterval`, `_overridingInterval`, `_overridingBurstSize` are all `[SerializeField]` — no recompile needed.

**Prop set curation pass** *(creative call)*
- 833 props includes `lab` glassware and `workshop` machinery that may not read as "everyday gallery objects." Brief says "real-world daily life props."
- Consider cutting `lab` and `workshop` groups → ~600 props. Edit `curated-props.json` or rerun the Python filter.
- Display names are auto-generated (e.g. "Binderbluelabel01A") — consider a pass to make them legible.

**Windows build**
- Build Settings already lists `SampleScene.unity`. Platform switch to Windows x64 from `File → Build Settings`, then `Build And Run`.
- Verify GLB streaming path resolves on Windows (it should — `Application.streamingAssetsPath` is platform-aware).
- Add hallway scene to build list before scene is done.

**Ghost preview upgrade**
- Current ghost is a white cube placeholder. A proper translucent version of the actual prop model is the right UX signal.
- Needs `RuntimeThumbnailCapture` to produce a world-space preview instance on a dedicated ghost layer, or a cheap scale-down of the loaded model with a ghost material applied.

**Spatial diegetic onboarding**
- Brief: "visitor is encouraged to build through diegetic spatial elements."
- Nothing in the sandbox communicates "place things here" yet. Options: a glowing floor grid, text that fades after first placement, a subtle arrow/glow on the first hotbar slot.
- Small UX addition, high concept payoff.

### Lower priority / post-A4

- VR port (architecture is pose-agnostic; swap `SimplePlayerRig` for an XR rig)
- Sound design pass (AudioSpectrumAnalyzer.cs exists from v1)
- Multiple ending variants based on player engagement level

---

## Work allocation — current state

| Track | Owner | Status |
|---|---|---|
| Style tracking, AssistantSystem, hotbar logic, PropPlacer, session export, performance | Ezra | ✅ Code complete |
| PSX shaders | Ezra | ✅ Code complete, ❌ not registered |
| Curated prop manifest | Ezra | ✅ Done (833 props) |
| Scene setup (SandboxBootstrap in scene) | Ezra / either | ❌ 2-minute task |
| 3D models / custom assets | Alyssa | ? |
| Hotbar UI visual design | Alyssa | ✅ Functional; needs visual polish |
| Hallway graybox + spatial design | Alyssa | ❌ Not started |
| Hallway past-session frames (code) | Ezra | ❌ Not written |
| Sandbox room layout | Together | ❌ Not started |
| Session screenshot at end | Either | ❌ 10 lines, not written |
| Branching end card | Ezra | ❌ Structure ready, copy/logic not written |
| Playtest tuning | Together | ❌ Needs a build first |
| Windows build | Ezra | ❌ Needs hallway scene first |

---

## Smallest version that still proves the idea

If scope needs to cut, the irreducible core is:

1. Empty sandbox room (auto-bootstrapped — works today)
2. 5-slot hotbar with prop placement (works today)
3. Assistant escalating over 90 s (works today)
4. End card showing "it placed more than you did" (works today)
5. PSX glitch ramping with influence (works, needs renderer feature registered)

Everything else — hallway, framed pedestals, screenshot, branching endings — is additive. The concept reads without them.

---

## Immediate next steps (in order)

1. **Ezra** — Open `SampleScene`, add `SandboxBootstrap`, press Play. Verify the loop runs end-to-end. This unblocks everything.
2. **Either** — Add `PSXRendererFeature` to `PC_Renderer.asset`. Confirm glitch ramps up.
3. **Ezra** — Add `ScreenCapture.CaptureScreenshot` call to `SandboxManager.OnSessionComplete`. Screenshots save to desktop.
4. **Alyssa** — Graybox the hallway + sandbox room. Wire `HallwayTrigger` at the threshold.
5. **Together** — First playtest. Tune timing values in the inspector. Decide on prop cuts.
6. **Ezra** — Write `HallwayGallery.cs` to render past-session frames once the hallway room exists.
7. **Ezra** — Windows build once the scene loop is stable.
