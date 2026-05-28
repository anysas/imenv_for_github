# Linear Room Loop - Editor Setup (April 16, 2026)

This runbook configures the new forward-only room loop:
- one direction of travel
- current room + 2 rooms ahead buffered
- one room spawned at the tail on each advance
- old rooms recycled behind the player

## 1) Prefab setup (`GalleryPrefab`)

On the prefab root, ensure `RoomRuntime` is attached and wire:
- `_leftSculptureSlot` -> `Pedastal_1`
- `_rightSculptureSlot` -> `Pedastal_2`
- `_nextRoomSpawnPoint` -> an empty transform placed at the forward exit alignment point

Trigger setup:
- Add one trigger collider in the forward passage where progression should happen.
- Add `RoomDoorTrigger` to that trigger object.
- Set `Trigger Type` to `Advance`.
- Keep `Require Player Tag` enabled and tag the player object as `Player`.

Entry setup:
- Keep or add one entry trigger with `RoomDoorTrigger` type `Entry` near room entrance.
- Ensure `RoomRuntime` references:
  - `_entryTrigger` -> entry trigger component
  - `_advanceTrigger` -> forward trigger component

Compatibility notes:
- If a prefab still uses old left/right trigger fields, they continue to work as advance fallback.

## 2) Scene setup (`RoomLoopManager` + bridge)

On the scene manager object with `RoomLoopManager`:
- `_roomPrefab` -> your simplified room prefab
- `_galleryBridge` -> scene `RoomGalleryBridge`
- `_roomsAheadBuffer` -> `2`
- `_roomsBehindKeep` -> `1`
- `_maxActiveRooms` -> `4` (minimum for current + 2 ahead + 1 behind)
- `_fallbackForwardSpacing` -> use only as backup when `_nextRoomSpawnPoint` is missing

Startup mode options:
- Managed-from-start: set `_enabledAtStart = true`.
- Legacy handoff flow: keep `_enabledAtStart = false` and use `LegacyRoomTransitionTrigger` to call `BeginManagedLoopFrom(...)`.

On `RoomGalleryBridge`:
- `_galleryManager` -> scene `GalleryManager`

## 3) Play mode validation checklist

1. Enter Play mode.
2. Confirm room chain count is 3 on startup (current + 2 ahead).
3. Walk through forward trigger once:
   - current room advances to next buffered room
   - pedestals rebind and spawn in the new current room
   - exactly one new room appears at the chain tail
4. Continue for 6-10 transitions:
   - no null-reference errors
   - no overlapping misalignment between rooms
   - no pedestal starvation (new current room always receives sculptures)
5. Confirm old rooms are recycled behind the player according to `_roomsBehindKeep`.

## 4) Troubleshooting

- If rooms stop spawning:
  - verify `_nextRoomSpawnPoint` is assigned in the room prefab.
  - check for warnings from `RoomLoopManager` about fallback spacing.
- If triggers do nothing:
  - verify collider is set to `isTrigger = true`.
  - verify player object tag is `Player`.
  - confirm trigger `RoomDoorTrigger` references a `RoomRuntime` in parent hierarchy.
- If sculptures do not appear in new rooms:
  - verify `RoomGalleryBridge` has a valid `GalleryManager` reference.
  - verify both pedestal transforms are assigned on `RoomRuntime`.
