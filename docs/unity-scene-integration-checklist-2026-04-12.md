# Unity Scene Integration Checklist (April 12, 2026)

Use this checklist to finish implementing the Algorithmic Gallery scripts in-scene and validate the full runtime loop.

## 1) Verify required files and packages
- Confirm scripts exist under `Assets/scripts`.
- Confirm `Assets/StreamingAssets/metadata.json` exists.
- Confirm `Assets/StreamingAssets/models/portal/...` contains `.glb` files.
- In Package Manager, confirm:
  - `com.atteneder.gltfast`
  - `com.unity.nuget.newtonsoft-json`

## 2) Create and wire scene objects
- Main Camera:
  - Add `DesktopGazeProvider`
  - Add `GazeManager`
- Create empty `GallerySystem` object:
  - Add `GalleryManager`
  - Add `SculptureSpawner`
- Create at least 3 empty pedestal transforms in the scene:
  - `Pedestal_1`, `Pedestal_2`, `Pedestal_3`
  - Position them where sculptures should spawn
  - Assign these transforms to `GalleryManager` -> `_pedestalSlots`
- Create empty `DebugUI` object:
  - Add `RecommendationDebugUI`

## 3) Configure critical inspector references
- In `GalleryManager`:
  - `_spawner` should reference the same object's `SculptureSpawner`
  - `_metadataJsonPath` should be `metadata.json`
- In `GazeManager`:
  - `_galleryManager` should reference scene `GalleryManager`

## 4) Run first playtest (desktop)
- Enter Play Mode.
- Confirm console logs:
  - Metadata loaded count from `GalleryManager`
  - Spawn log entries for pedestals
- Look at a sculpture long enough to trigger bloom (`~0.6s`).
- Look away and confirm:
  - Decay begins
  - Gaze exit log appears
  - Recommendations continue updating
- Press `F1` to verify debug panel:
  - Phase and progress
  - Session stats
  - Top preference weights

## 5) If spawn/load fails
- Check `metadata.json` model entries point to valid relative paths under `StreamingAssets/models/`.
- Validate that referenced `.glb` files actually exist.
- Confirm GLTFast package is installed and resolved by Unity.

## 6) Prototype-ready gate
- No C# compile errors in Console.
- At least 3 pedestals continuously populated.
- Gaze interaction visibly affects sculpture state.
- Debug UI updates phase and preference values during session.

## 7) Continue development
- For a full summary of implemented changes and next priorities, read:
  - `docs/development-update-2026-04-12.md`
