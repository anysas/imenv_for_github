# Immersive Environments: Comprehensive Project Overview & History

**Last updated:** 2026-05-14  
**Verification note:** Counts and defaults below are taken from the repository on this date; manifest sizes and Inspector-tuned values can change without code edits.

**Course:** Immersive Environments, RMIT University  
**Author:** Ezra Johnston (RMIT Digital Media Design) & project team

This document is the unified reference for the Unity project **Immersive Environments** (in-class framing: **Algorithmic Gallery v2**; working title: **Project Evil Minecraft**). It covers conceptual intent, historical pivots, technology, data/tooling, **verified current runtime behaviour**, and remaining gaps.

---

## 1. Core concept and themes

The piece is an interactive artwork / game prototype that critiques **algorithmic curation**, **platform logic**, and **loss of creative agency**—summarised in working language as: **the force is misinterpretation** (see also [A3 development presentation](a3-development-presentation.html)).

### Evolution of the critique

- **V1 — Algorithmic Gallery (passive observation):** Early direction used gaze-driven recommendation across procedurally generated sculpture, with phased emotional escalation. Legacy code remains under [`Assets/scripts/_legacy/`](../Assets/scripts/_legacy/) (`RecommendationEngine`, `GazeManager`, `GalleryManager`, room-loop experiments).
- **V2 — Sandbox / “Evil Minecraft” (active placement):** The project pivoted to **active building**: the visitor enters a sandbox, commits a personal prompt, and places props from a large curated library. An **assistant** (stand-in for platform algorithms) escalates placement pressure. Corporate optimisation language and scoring are meant to read as **platform vocabulary**, not neutral game UI.

**Central theme:** Misinterpretation is intentional critique—semantic flattening and category collapse show how optimisation-oriented systems **cannot faithfully hold** specific human intent; the gap is part of the argument, not a bug to “solve away” entirely (see [project-current-state.md](project-current-state.md) § design intent).

---

## 2. Chronological history and major pivots

### Era 1 — V1 attention gallery (early–mid April 2026)

- **Focus:** Gaze tracking, procedural sculpture, three-phase recommendation.
- **Pain points:** Procedural linear room-loop instability (drift, overlap) documented in earlier session notes.

### Era 2 — V2 sandbox pivot (late April 2026; presentation window ~2026-04-25–04-28)

- **Focus:** Shift from “observe + recommend” to **“build + be overridden”**.
- **Core stack introduced:** `SandboxManager`, `AssistantSystem`, `PropPlacer`, `HotbarController`, `StyleProfile`, session export, PSX-style presentation stack.

### Era 3 — Readability, content scale, and loop closure (May 2026)

- **Focus:** Make the critique **legible during play** (prompt squish, scoring wall, hallway return), scale **curated** props, and close the **hallway ↔ sandbox ↔ export** loop with **pedestal diorama replay** (not wall snapshot textures).
- **Spatial integration:** Merged spatial content into **`CreationGallery.unity`** (collaboration track: spatial/UX vs behaviour/integration—see [A3 development presentation](a3-development-presentation.html) slide 6).

---

## 3. Technology stack

Verified from [`ProjectSettings/ProjectVersion.txt`](../ProjectSettings/ProjectVersion.txt) and [`Packages/manifest.json`](../Packages/manifest.json):

| Layer | Choice |
|--------|--------|
| **Engine** | Unity **6000.1.3f1** (Unity 6) |
| **Rendering** | **URP** `com.unity.render-pipelines.universal` **17.3.0** |
| **Runtime models** | **GLTFast** `com.unity.cloud.gltfast` **6.18.0** (async `.glb` load from StreamingAssets) |
| **JSON** | **Newtonsoft.Json** `com.unity.nuget.newtonsoft-json` **3.2.2** |
| **Input** | **Input System** `com.unity.inputsystem` **1.18.0** |
| **Also in project** | ProBuilder, AI Navigation, Timeline, TMP, Visual Scripting, etc. |

**Offline tooling:** Python scripts at repo root and under [`tools/`](../tools/) drive manifest curation, vocabulary, game-ready splits, optional model pruning, and tagging experiments. **Bulk `.blend` → `.glb` export** (e.g. Blender batch pipelines) is **out-of-repo**; this repository assumes **GLBs + manifests** as inputs.

---

## 4. Asset and data pipeline (current)

### Source corpus

- Large **Source-engine-derived** GLB library under [`Assets/StreamingAssets/models/`](../Assets/StreamingAssets/models/) (on the order of **~21,800** `.glb` files when last inventoried; exact count drifts with pruning/archives).
- **Authoring manifest:** [`Assets/StreamingAssets/curated-props.json`](../Assets/StreamingAssets/curated-props.json) — canonical curated rows used by tooling and (by default) runtime fallback.
- **Supporting data:** `metadata.json`, `vocabulary.json`, optional `curation_overrides.json` (review/removal overlay for curation workflow — see [game-ready-manifest.md](game-ready-manifest.md)).

### In-repo Python (representative)

**Root:** `curate_pipeline.py`, `expand_manifest.py`, `remove_oversized_from_manifest.py`, `generate_vocabulary.py`, `validate_vocabulary.py`.

**`tools/`:** `split_game_ready_manifest.py`, `prune_streaming_models.py`, `tools/tagging/*` (taxonomy, balance, gates, reports, etc.).

### Game-ready subset and build hygiene

- **Split:** `python3 tools/split_game_ready_manifest.py` → `curated-props.game-ready.json` + `curated-props.non-ready.json` (+ report under `curation-reports/`). Definitions and rollback: [game-ready-manifest.md](game-ready-manifest.md).
- **Runtime:** `SandboxManager` prefers the game-ready file when present and non-empty; otherwise falls back to `curated-props.json` (see §6).
- **Prune (optional):** `tools/prune_streaming_models.py` can archive unreferenced GLBs; see [game-ready-manifest.md](game-ready-manifest.md) for safety flags and paths.

---

## 5. Repository map (top level)

| Path | Role |
|------|------|
| [`Assets/scripts/`](../Assets/scripts/) | Primary C# (**104** `.cs` files on 2026-05-14, including `_legacy/`, `Editor/`, shader-adjacent folders) |
| [`Assets/Scenes/`](../Assets/Scenes/) | **Seven** shipped scenes (see §7) |
| [`Assets/AssetCuration.unity`](../Assets/AssetCuration.unity) | Curation / tooling scene (not the main playtest loop) |
| [`Assets/StreamingAssets/`](../Assets/StreamingAssets/) | Manifests, vocabulary, `models/`, `seed_sessions/` |
| [`Assets/Shaders/`](../Assets/Shaders/) | PSX, CRT, glitch, fog-related shader assets |
| [`Assets/Settings/`](../Assets/Settings/) | URP and project render settings |
| [`docs/`](../docs/) | Specs, wiring notes, session logs, presentation deck |
| [`tools/`](../tools/) | Python utilities (split, prune, tagging) |
| [`archive/`](../archive/) | Older snapshots; not treated as primary runtime |
| [`Packages/`](../Packages/), [`ProjectSettings/`](../ProjectSettings/) | Unity package lock and editor/project config |

**Note:** `Trial1.unity` appears in some older notes but **is not present** in this repository; the integrated hallway + sandbox + export wiring is documented for **`CreationGallery.unity`** in [diorama-gallery-scene-wiring.md](diorama-gallery-scene-wiring.md).

---

## 6. Verified runtime architecture (`AlgorithmicGallery.Corruption`)

### Orchestration and scene bootstrap

- **`SandboxBootstrap`** — ensures a `SandboxManager` exists in scene if configured.
- **`SandboxManager`** — loads manifest, builds `StyleProfile`, wires `PropPlacer`, `HotbarController`, `AssistantSystem`, scoring labels, session events (`OnSandboxEntered`, `OnSessionComplete`, prompt events). Can auto-bootstrap missing references when `_autoBootstrap` is enabled.
- **`IntakeRoomGateController`**, **`DoorOpen`** — intake / door choreography (spatial flow with merged Creation Gallery content).
- **`HallwayManager`**, **`HallwayTrigger`**, **`HallwayDioramaPedestal`**, **`DioramaProximityTrigger`** — hallway pedestals, live pedestal refresh, proximity-based reload (see §8).

### Player interaction

- **`PropPlacer`** — raycast placement, ghost preview, registration with `StyleProfile` / optional `PropBudget`.
- **`HotbarController`** — **3 slots**; number keys **1–3**; after each successful placement a short **“thinking”** delay runs, then slots reroll and selection advances (see file header comments in [`HotbarController.cs`](../Assets/scripts/HotbarController.cs)).
- **`ThemeSelectionUI`** — prompt entry and commitment flow.

### Session end condition (authoritative)

- **`SandboxManager`** ends the sandbox when **total placements (player + assistant)** reach **`_maxTotalPlacements`** (serialized default **25**). `OnSessionComplete` fires; optional fade / hotbar fade are configured on the component.
- **`_endGracePeriod`** exists but defaults to **0** in code—do not rely on a long grace period unless set in the scene.

This **replaces** older narrative that described the run as primarily “~90 seconds then stop”: **90s** remains meaningful as **`AssistantSystem._sessionDuration`** for **influence / phase / visuals**, but **session termination** in the current default tuning is **placement-capped**, not timer-first.

### Assistant behaviour (authoritative)

From [`AssistantSystem.cs`](../Assets/scripts/AssistantSystem.cs):

- **Activation:** Random threshold per session between **`_activationMinPlayerPlacements`** and **`_activationMaxPlayerPlacements`** (defaults **8** and **12** inclusive roll). Not “after exactly five player props.”
- **Phases:** Helping → Suggesting → Overriding still exist; **influence** advances from session time (default `_sessionDuration` **90s**) for visuals / escalation feel.
- **Corporate-only picks (when a prompt is active):** `PickPropForPhaseWithPrompt()` uses manifest weighting **`GetWeightedByCorporateTagInGroups`** — explicitly **without** `StyleProfile` weighting for assistant prop choice (style remains relevant for **player** hotbar rerolls and analytics).
- **Assistant placement cap:** **`_maxAssistantPlacements`** (default **8**) limits assistant-owned placements per session; total session length is still bounded by `SandboxManager` total placement cap.

### Manifest loading in sandbox

- **`_preferGameReadyManifest`** (default **true**): load `curated-props.game-ready.json` if file exists and parses with **count > 0**; else warn and fall back to **`_fallbackManifestFileName`** (default full `curated-props.json` via `CuratedPropManifest.DefaultManifestFileName`).

### Scoring and diegetic UI

- **`ScoringWallController`** — three vertical bars (default max score **1500**); wired from `SandboxManager` (`ScoringBarLabels`, personal slugs, corporate slug). Recent work emphasised **corporate vs personal** readability, labels, and **placement score floaters** (`PlacementScoreFloater`).
- **`ProfileTerminalDisplay`** — additional diegetic terminal-style readout where used in scene; do not conflate it with the scoring wall unless the scene actually mounts both for the same beat.

### Prompt pipeline

- **`PromptParser`**, **`PromptDefinition`**, **`PromptScoringHelper`** — structured prompt, collapsed terms, corporate target tag, etc. **Prompt squish** copy should be described as **data-driven** (collapsed terms from the parser), not three fixed literals—fixed examples like `domestic` / `furniture` / `item` are illustrative only.

### Persistence and analysis

- **`SessionExporter`** — on `SandboxManager.OnSessionComplete`, writes `Application.persistentDataPath/sessions/session_<unix>.json`, updates `index.json`. Payload includes **`SchemaVersion`** (`SessionRecord.CurrentSchemaVersion` is **3** at time of writing). **v2+** adds `SandboxOrigin` and per-placement rotation/scale for pedestal replay; **v3+** also records **sandbox floor rotation/scale** for tighter replay alignment (see comments on `SessionRecord` in [`SessionExporter.cs`](../Assets/scripts/SessionExporter.cs)).
- **`StyleProfile`** — placement history, counts, cadence, dominant tags/groups for export and UI.

### Performance / prop budget

- **`PropBudget`** — optional cap; defaults **`_maxPlacedProps = int.MaxValue`** and **`_autoEvictWhenOverBudget = false`**. Eviction-at-150 is **not** a code default; if a scene needs hard caps, that is **Inspector configuration**.

### Rendering and VFX

- **URP renderer features:** `PSXRendererFeature`, `PSXPass`, dithering / pixelation / fog / CRT / glitch passes under [`Assets/scripts`](../Assets/scripts/) subfolders.
- **`SandboxReactiveVfxDirector`**, **`EnvironmentFXManager`**, **`AudioEscalation`** — event- and phase-driven intensity.

### Legacy

- **`Assets/scripts/_legacy/`** — V1 gaze/recommendation/room-loop experiments; kept for reference, not the active product direction.

---

## 7. Unity scenes (current files)

Under [`Assets/Scenes/`](../Assets/Scenes/) (seven files):

| Scene | Typical role |
|-------|----------------|
| **`CreationGallery.unity`** | **Primary integrated loop:** hallway, intake, sandbox, scoring wall, session export, pedestal replay wiring (see [diorama-gallery-scene-wiring.md](diorama-gallery-scene-wiring.md)) |
| `HallToSandbox_Sculptures.unity` | Alternate / sculpture hallway experiment |
| `SampleScene.unity` | Template / tests |
| `Psx_PBR.unity`, `Psx_Unlit.unity`, `Psx_Unlit_Variant.unity`, `Psx_Unlit_Variant_PolyBrush.unity` | PSX look-dev variants |

Additional: [`Assets/AssetCuration.unity`](../Assets/AssetCuration.unity) for curation viewport / lab workflows.

---

## 8. Current implemented experience (hallway → sandbox → return)

High-level flow (aligned with [diorama-gallery-scene-wiring.md](diorama-gallery-scene-wiring.md) and `CreationGallery`):

1. **Hallway** — visitor moves through space; UI may stay minimal until triggers fire (scene-tuned).
2. **Prompt** — visitor commits text; **prompt squish** runs (`SandboxManager` events such as `OnPromptSquishStarted` / prompt commit pipeline).
3. **Sandbox** — **3-slot hotbar**, ghost preview, **left-click place** (scene/input wiring); **placements remaining** surfaced near hotbar in recent UX passes.
4. **Assistant** — activates after **rolled** player-placement threshold (defaults 8–12); autonomous + reactive placements subject to **`_maxAssistantPlacements`**; influence/time still drives **phase / VFX** feel.
5. **Session end** — at **total placement cap** (default **25**), `OnSessionComplete` runs; exporter writes JSON; hallway can refresh **live pedestal** after configured delay.
6. **Return / loop** — visitor walks to **live pedestal** `DioramaProximityTrigger`; after linger (e.g. **10s** in wiring doc), fade and **reload scene**; archived sessions + seeds populate other anchors via `HallwayManager`.

**Pedestal replay:** `HallwayDioramaPedestal` uses **exact transform replay** when session **`SchemaVersion` ≥ 2** (legacy footprint layout below that). New exports use **schema 3** when the runtime matches current `SessionExporter` defaults.

**Title / menu:** `TitleScreenUI` is treated as **player POV overlay** (no separate title camera required for that flow)—see wiring doc.

---

## 9. Known gaps and planned work (backlog)

Consolidated from [project-current-state.md](project-current-state.md), [a3-development-presentation.html](a3-development-presentation.html) (risks / next steps), and open design notes—not all are implemented:

- **Readability:** clearer “lockout” / loss-of-agency signalling; optional **countdown** before build ability ends (design intent vs current `SandboxManager` defaults).
- **Spatial discipline:** stricter **platform-only** placement, anti-stack / overlap rules (partially aspirational).
- **Content meaning:** continued **manual curation**, taxonomy balance, prompt→prop relevance (game-ready workflow supports this).
- **Polish:** hallway **force** presence / profiling cues; renderer registration edge cases; Windows build validation.
- **Not the current direction:** wall **`HallwayGallery`** texture snapshots—replaced by **JSON-driven pedestal dioramas** + GLB replay.

---

## 10. Recent change log (May 2026)

Cursor parent chats (UUID without `.jsonl` extension) reflecting work merged into the trajectory of this doc:

| Topic | Transcript |
|--------|------------|
| Merge Creation Gallery spatial content | [Merge Alyssa’s Creation Gallery](7f8902f1-26c1-4d44-9ded-a85fd2e721ca) |
| Game-ready manifest, split, prune / debloat | [Balanced tagging & debloat](b5895544-6379-49f2-98b1-f9d445a93e30) |
| Hotbar UX, 3 slots, thinking delay, 25 placement session end | [Hotbar UX & placement cap session](6896767d-49fd-4c26-8014-546d55dd3dda) |
| Session export, pedestal replay, proximity reload, title POV | [Diorama gallery loop](09b899a6-445d-44b0-baf1-4a4486ee6885) |
| Placements-left HUD on hotbar | [Placements-left HUD placement](724d2f0a-1472-43ed-b318-4b0df3d9573f) |
| Scoring wall bars, floaters, corporate assistant picks | [Scoring system & corporate assistant](5fc95e8b-08f0-4d91-ab8e-dc5fe364646f) |
| Scoring wall labels / TMP layout | [Scoring wall label fix](1cb43345-b682-4fe6-ae7f-7530da856f5b) |

---

## 11. Related documentation index

| Document | Use |
|----------|-----|
| [project-current-state.md](project-current-state.md) | Dated snapshot, backlog, design rationale |
| [game-ready-manifest.md](game-ready-manifest.md) | Game-ready split + prune |
| [diorama-gallery-scene-wiring.md](diorama-gallery-scene-wiring.md) | `CreationGallery` object wiring |
| [a3-development-presentation.html](a3-development-presentation.html) | Pitch framing, timeline, critique questions |
| [project-brief-updated-2026-04-26.md](project-brief-updated-2026-04-26.md) | Brief (may predate latest wiring) |
| [session-log.md](session-log.md) | Older dated session entries |

---

## 12. Credits and references (from project docs)

- PSX filtering reference base (credit): `https://github.com/Math-Man/URP-PSX-FORKED`
- Source model corpus reference: `https://github.com/hisprofile/blenderstuff/blob/main/Creations/Source%20Engine%20Blender%20Collection.md`

---

*End of document.*
