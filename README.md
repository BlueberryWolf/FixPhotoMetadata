# FixPhotoMetadata

A Resonite Mod using Harmony to fix two bugs in the game's photo metadata collection system:

1. **Incorrect Camera FOV Timing**: Captures the precise camera FOV at the exact moment a photo is initiated, preventing default/delayed values from being saved if the user breaks the finger gesture before rendering completes.
2. **Incorrect TakenGlobal Coordinates**: Overwrites `TakenGlobalPosition` and `TakenGlobalRotation` with the actual camera/view coordinates at the moment the capture starts, instead of the unpositioned photo slot's default coordinates (which resulted in `0` or user root coordinates).

## Dynamic Optional Integrations

### GalleVR / ResoniteScreenshotExtensions (aka "ScreenshotExtensionsExtensions" LOL)

If `ResoniteScreenshotExtensions` is also installed/loaded, this mod dynamically patches it at runtime (without static dependencies) to add extra metadata fields:

- **Visibility & Occlusion Check**: Checks if players are in the camera view and runs raycasts to filter out players hidden behind walls/objects (ignoring triggers, camera slots, and self-avatar colliders).
- **Head Bounding Box Scaling**: Injects `UI-HeadScale` and `UI-IsInView` as separate attributes.
- **Viewers Compatibility**: Keeping the attributes separate means `UI-HeadPosition` remains a standard `[x; y; z]` value, so it doesn't break other XMP metadata viewers.

This can be disabled in the mod settings under `InjectOcclusion`.

## Requirements

- [Resonite](https://resonite.com/)
- [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader)

## Installation

1. Compile the class library or download the compiled `FixPhotoMetadata.dll`.
2. Move `FixPhotoMetadata.dll` into your Resonite installation's `rml_mods` folder (typically `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods`).
3. Start the game.

