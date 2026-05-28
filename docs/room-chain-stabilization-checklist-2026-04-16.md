# Room Chain Stabilization Checklist (April 16, 2026)

Use this checklist to verify the stabilized linear room chain implementation.

## Scene contract (must pass before Play)

1. Exactly one `GalleryManager` exists in the scene.
2. Exactly one `RoomGalleryBridge` exists in the scene.
3. Exactly one `RoomLoopManager` exists in the scene.
4. `RoomLoopManager._roomPrefab` points to the managed room prefab.
5. `RoomLoopManager._galleryBridge` points to scene `RoomGalleryBridge`.
6. `RoomLoopManager._placementMode` is `TriggerAnchorChain` (default production mode).
7. Legacy trigger object has `LegacyRoomTransitionTrigger` and:
   - `_roomLoopManager` assigned
   - `_spawnPointOverride` assigned
   - `_requireSpawnPointOverride` enabled

## Prefab contract (managed room prefab)

On prefab root (`RoomRuntime`):
- `_nextRoomSpawnPoint` assigned explicitly
- `_roomEntryAnchor` assigned explicitly
- `_leftSculptureSlot` assigned explicitly
- `_rightSculptureSlot` assigned explicitly
- `_entryTrigger` assigned explicitly
- `_advanceTrigger` assigned explicitly
- `_allowAutoAssignBindings` can remain true, but explicit assignments are required for stable production behavior

## 10-step progression verification

1. Enter Play mode in legacy room.
2. Cross `LegacyRoomTransitionTrigger`.
3. Confirm logs:
   - `LegacyRoomTransitionTrigger: Managed handoff started...`
   - `RoomLoopManager: Managed loop started... placementMode=TriggerAnchorChain`
4. Confirm 3 managed rooms exist (`current + 2 ahead`).
5. Confirm each spawn log includes `poseSource=TriggerAnchorChain`.
6. Enter managed room 1 and cross advance trigger.
7. Confirm current-room transition log:
   - `Current room changed X -> Y`
8. Confirm exactly two pedestal blends appear in active room.
9. Repeat steps 6-8 until 10 transitions complete.
10. Confirm no warnings for:
   - missing `NextRoomSpawnPoint`
   - stale spawn ownership loops repeating constantly
   - missing bridge/gallery references

## Expected behavior after stabilization

- Rooms chain from explicit `NextRoomSpawnPoint` anchors only.
- New rooms align by `RoomEntryAnchor` in `TriggerAnchorChain`.
- No deterministic lattice behavior unless mode is manually changed to `DeterministicDebug`.
- Async sculpture spawns abort safely if slot ownership changes during room transitions.
