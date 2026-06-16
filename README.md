# FixPhotoMetadata

A Resonite Mod using Harmony to fix two bugs in the game's photo metadata collection system:

1. **Incorrect Camera FOV Timing**: Captures the precise camera FOV at the exact moment a photo is initiated, preventing default/delayed values from being saved if the user breaks the finger gesture before rendering completes.
2. **Incorrect TakenGlobal Coordinates**: Overwrites `TakenGlobalPosition` and `TakenGlobalRotation` with the actual camera/view coordinates at the moment the capture starts, instead of the unpositioned photo slot's default coordinates (which resulted in `0` or user root coordinates).

## Requirements

- [Resonite](https://resonite.com/)
- [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader)

## Installation

1. Compile the class library or download the compiled `FixPhotoMetadata.dll`.
2. Move `FixPhotoMetadata.dll` into your Resonite installation's `rml_mods` folder (typically `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods`).
3. Start the game.
