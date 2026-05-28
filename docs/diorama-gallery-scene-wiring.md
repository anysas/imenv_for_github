# Diorama gallery loop — scene wiring (`CreationGallery`)

This documents how the **hallway live pedestal**, **session export**, and **exact replay** work together.

## Runtime flow

1. `SessionExporter` writes `Application.persistentDataPath/sessions/<id>.json` and updates `index.json` on `SandboxManager.OnSessionComplete`. New sessions use **schema v2** (`SchemaVersion: 2`) with `SandboxOrigin` and per-placement `Rotation` / `Scale` for one-to-one pedestal replay.
2. `HallwayManager` listens for the same event and, after `_livePedestalRefreshDelay`, spawns a `HallwayDioramaPedestal` on **`_livePedestalAnchor`** (the pedestal that stayed empty at intro).
3. The player walks back; `DioramaProximityTrigger` (child of the live pedestal) starts its countdown once the session is complete and the player stands in the trigger, then fades and reloads the active scene.
4. On reload, `HallwayManager` fills the non-live anchors from `index.json` / seeds; the newest session appears on the live slot again after the *next* completion (or you can treat the row as “history + empty live” depending on anchor count).
5. `TitleScreenUI` is player POV (transparent overlay + Begin); no separate title camera required.

## Exact replay (schema v2)

- [`SessionExporter.cs`](../Assets/scripts/SessionExporter.cs) saves each placement’s **world position**, **world rotation**, **local scale** (spawn root), plus **`SandboxOrigin`** = `SandboxManager.SandboxFloor.position` at export time.
- [`HallwayDioramaPedestal.cs`](../Assets/scripts/HallwayDioramaPedestal.cs) detects `SchemaVersion >= 2` and places each GLB at  
  `anchor.position + anchor.up * yOffset + (savedPos - sandboxOrigin) * _uniformShrink`  
  with rotation and scale multiplied by the same **`_uniformShrink`**.
- Pre-v2 sessions and shipped **`seed_sessions/*.json`** use the **legacy** footprint-fit layout (no rotation/scale in file).

Tune **`_uniformShrink`** on the `HallwayDioramaPedestal` component (or defaults in script) so the cluster fits your pedestal size.

## `CreationGallery.unity` — what is set up

| Item | Location / notes |
|------|------------------|
| **Hallway pedestal root** | GameObject **`premadepedestals`** — assigned to `HallwayManager._pedestalsRoot`. Child transforms are anchor positions for dioramas. |
| **Live (empty-at-intro) pedestal** | Child **`LivePedestal`** — assigned to `HallwayManager._livePedestalAnchor`. `HallwayManager` skips this anchor when picking anchors for archived sessions so it starts empty. |
| **Proximity reload** | **`DioramaProximityTrigger`** is parented under **`LivePedestal`**. It uses `_onlyActiveAfterSessionComplete: true`, `_triggerDelay: 10`, `_fadeDuration: 2`. Collider is a trigger `BoxCollider` on the same GameObject. |
| **`SessionExporter`** | Scene object **`SessionExporter`** — references **`SandboxManager`** on `SandboxBootstrap` so exports always run. |
| **`HallwayManager`** | Scene object **`HallwayManager`** — references `_pedestalsRoot`, `_livePedestalAnchor`, `_maxSessions`, etc. |

## Optional polish

- Assign `HallwayManager._pedestalMeshPrefab` to [`premadePedestal (2).prefab`](../Assets/premadePedestal%20(2).prefab) so each anchor gets a visible base mesh (if not already modeled in the scene).

## Code references

- [`SessionExporter.cs`](../Assets/scripts/SessionExporter.cs) — JSON persistence (schema v2)  
- [`StyleProfile.cs`](../Assets/scripts/StyleProfile.cs) / [`PropPlacer.cs`](../Assets/scripts/PropPlacer.cs) — placement history with transforms  
- [`HallwayDioramaPedestal.cs`](../Assets/scripts/HallwayDioramaPedestal.cs) — exact vs legacy replay  
- [`HallwayManager.cs`](../Assets/scripts/HallwayManager.cs) — anchors, live pedestal refresh  
- [`DioramaProximityTrigger.cs`](../Assets/scripts/DioramaProximityTrigger.cs) — linger → fade → reload  
