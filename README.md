# MusicBox

![Vibe Coding](https://img.shields.io/badge/Vibe%20Coding-100%25-blueviolet)

A BepInEx 6 (IL2CPP) music expansion mod for **MVZ2 (Minecraft VS Zombies 2) / MVZ2Expand**.

It lets players add custom music packs, always shows the Music Room, and customizes level / map background music per scene.

## Table of Contents

- [Overview](#overview)
- [Vibe Coding](#vibe-coding)
- [Features](#features)
- [Installation](#installation)
- [Usage](#usage)
- [Data Format](#data-format)
- [Building from Source](#building-from-source)
- [Compatibility](#compatibility)
- [Known Limitations](#known-limitations)
- [Development Notes](#development-notes)
- [Credits](#credits)
- [License](#license)

## Overview

MusicBox injects into the game at runtime via BepInEx and provides:

- player-supplied custom music packs (packaged like language packs);
- the Music Room is always available, ignoring progress unlocks;
- management of music packs from the in-game "Addons" screen;
- per-scene battle / map background music configuration from the Music Room.

## Vibe Coding

This project is developed with **Vibe Coding**: the code and documentation are written primarily by prompting AI assistants. Parts of the codebase are AI-generated. Please review and test before relying on it in your own setup.

## Features

- **Custom music packs**: drop them into `music_packs/`; folder or `.zip`, audio supports Vorbis and Opus `.ogg`.
- **Always-available Music Room**: bypasses progress unlocks (a read-only check; the save is not modified).
- **Music pack management**: import / export / delete / reorder from the Addons screen.
- **Custom battle music**:
  - chapter (main-line) battle music, including variants such as Dream / Nightmare;
  - per-phase music for Boss stages;
  - chapter map BGM, separated by map preset (e.g. Dream / Nightmare map).
- **Dual-track support**: map BGM can play the main track and continue with the sub track in a loop.
- **Asynchronous decoding + disk cache**: tracks are decoded on background threads and cached to disk (PCM), so subsequent launches skip OGG decoding entirely.
- **Dynamic scene resolution**: chapters / Boss stages / map presets are enumerated from game data at runtime, and only chapters unlocked in the current save are shown.

## Installation

1. Make sure the game has **BepInEx 6 (IL2CPP)** installed.
2. Copy the following 4 DLLs into `BepInEx/plugins/` (same folder):

   - `MusicBox.dll`
   - `NVorbis.dll`
   - `Concentus.dll`
   - `Concentus.Oggfile.dll`

3. Start the game. If the log shows `Plugin com.mvz2.musicbox is loaded!`, installation succeeded.

## Usage

### 1. Add a custom music pack

Put the pack under:

```
<game data directory>/music_packs/
```

Each pack is either **a folder** or **a `.zip`** (one extra nested folder is allowed). See [Data Format](#data-format).

Packs are loaded automatically on the next launch; you can also import a zip from the Addons screen.

### 2. Manage packs in the Addons screen

Open the "Addons" screen from the main menu. A "Music Pack" button appears (it reuses the language pack button's appearance). From there you can:

- import a `.zip` pack;
- export or delete a pack (delete asks for confirmation);
- move up / down (affects only the relative order of custom tracks in the Music Room).

### 3. Configure battle / map BGM

Enter the **Music Room** and click the map-options-styled button to open the configuration:

1. select a chapter;
2. select the item to configure: chapter music (each variant) / map music (each preset) / each Boss phase;
3. choose either the original track or a custom track.

Each slot can only be the **original music of that scene** or **a custom track**; you cannot pick built-in music from other scenes.

## Data Format

### Music pack

```
music_packs/<pack name>/      or <pack name>.zip
    pack.json                 { name, author, description, dataVersion }
    songs.json                { "songs": [ { ... } ] }
    *.ogg
```

`pack.json`:

```json
{
    "name": "Pack name",
    "author": "Author",
    "description": "Description",
    "dataVersion": 1
}
```

`songs.json`:

```json
{
    "songs": [
        {
            "id": "song_1",
            "name": "Track name",
            "file": "song_1.ogg",
            "subFile": "song_1_sub.ogg",
            "source": "Source",
            "origin": "Original",
            "author": "Author",
            "description": "Review text, multiple paragraphs allowed.\n\nSeparate paragraphs with blank lines."
        }
    ]
}
```

Field reference:

| Field | Required | Description |
| --- | --- | --- |
| `id` | yes | Unique within the pack; missing / duplicate rejects the **whole pack** |
| `name` | no | Display name in the Music Room; duplicates allowed |
| `file` | yes | Main track file, must be `.ogg` |
| `subFile` | no | Sub track file, must be `.ogg` |
| `source` / `origin` / `author` / `description` | no | Music Room info and review text |

### battle_music.json

Written and read automatically by the mod UI (stored at `music_packs/battle_music.json`); players normally do not need to edit it:

```json
{
    "areas":  { "mvz2:dream_level": "musicbox:xxx", "mvz2:nightmare_level": "musicbox:yyy" },
    "maps":   { "mvz2:dream_map": "musicbox:zzz" },
    "stages": { "mvz2:halloween_11": ["", "musicbox:yyy"] }
}
```

- `areas`: chapter battle music, keyed by the **vanilla battle track ID**;
- `maps`: map BGM, keyed by the **map preset music ID**;
- `stages`: Boss stage phases; the array index is the phase, an empty string falls back to the original;
- missing / empty = use the original music.

## Building from Source

Requirements: `.NET SDK` (target `net6.0`).

1. Put the game **interop DLLs** for the target version into the repo's `interop/` folder (this folder is `.gitignore`d), or specify it at build time with `-p:InteropDir=`.

2. Build:

```bash
dotnet build
# or point to a specific interop directory
dotnet build -p:InteropDir=/path/to/interop_mvz2expand
dotnet build -p:InteropDir=/path/to/interop_mvz2
```

Output is in `bin/Debug/net6.0/`. Deploy `MusicBox.dll`, `NVorbis.dll`, `Concentus.dll`, and `Concentus.Oggfile.dll` together.

### Dependencies

- `BepInEx.Unity.IL2CPP`, `BepInEx.PluginInfoProps`
- `NVorbis` (Vorbis decoding)
- `Concentus.Oggfile` / `Concentus` (Opus decoding)

## Compatibility

- Target framework: `net6.0` (BepInEx 6 IL2CPP / CoreCLR).
- Plugin GUID: `com.mvz2.musicbox`.
- **Vanilla and Expand**: comparing both interops, every type / member MusicBox uses has **identical signatures**, the interop assemblies share the same simple names, and BepInEx binds by name; therefore **a single DLL should work on both for the same build**. Version-specific content differences (e.g. the `_15` Boss stage that only exists in Expand) are handled by runtime enumeration.
- **Note: only the Expand build has been tested on a real device; vanilla has not been tested yet.**

## Known Limitations

- **Only the game's default language (Simplified Chinese) is supported; the mod does not support I18N yet.**
- The Boss phase track table is maintained statically; new bosses in the game require updating it.
- In non-Music-Room scenes (battle / map), the required custom track is decoded synchronously before playback; on a fresh install without a warm disk cache this may block briefly the first time. The Music Room decodes in the background and refreshes the shown track info once ready (e.g. `??:??` -> real duration).
- Music pack icons (`pack.png`) are not supported yet.
- For battle / Boss music, dual-track still follows the game's original logic (huge-wave crossfade); the "main then sub" order is not applied there.

## Development Notes

### Directory structure

```
MusicBox/
  Plugin.cs                     Plugin entry: Harmony, injects MusicBoxBehaviour
  MusicBoxBehaviour.cs          Injected MonoBehaviour (fallback startup driver)
  Core/MusicCore.cs             Core: MiniJson, registry, music pack loading/validation/decoding
  Patches/MusicHooks.cs         Music-related Harmony patches
  Patches/StartupLoadPatch.cs   Loads packs and battle config at startup
  Addons/MusicPacksPage.cs      "Music Pack" button + management page in the Addons screen
  Battle/BattleMusic.cs         Battle/map BGM config data + dynamic enumeration + runtime resolution
  Battle/BattleMusicPage.cs     Music Room config button + custom dialog
```

### Main logic

- **Startup**: `Plugin.Load` applies Harmony and registers the injected type; `Patches/StartupLoadPatch.cs` loads music packs and battle config after `MainManager.Initialize`.
- **Music packs**: `MusicPackLoader.Reload` scans / parses / validates (whole-pack rejection) and registers `MusicMeta` plus audio file paths. Audio is decoded **asynchronously** on background threads (OGG -> PCM, with a persistent on-disk PCM cache keyed by source file size + mtime), and the AudioClip is created on the main thread by a pump (`MusicManager.Update`). For non-Music-Room scenes (battle / map overrides), the required track is decoded synchronously before playback to guarantee it is ready.
- **Game integration**: patches to `ResourceManager.GetAllMusicID/GetMusicMeta/GetMusicClip` bring custom tracks into the Music Room; `LogicSaveExt.IsMusicRoomUnlocked` always returns `true` to keep the Music Room visible.
- **Battle / map BGM**: `BattleMusic` enumerates chapters / Boss stages / map presets at runtime and resolves the custom track for the current scene. When overriding, the **playing ID is not changed**; instead, during the `MusicManager.Play`/`SetPlayingMusic` call the meta for that ID is temporarily redirected to the custom track, so the game's own logic (e.g. Dream/Nightmare switching) keeps working.
- **Dual track**: for map BGM, the main and sub track PCM is concatenated into one looping AudioClip during decoding.

### Harmony patch list

| Target | Kind | Purpose |
| --- | --- | --- |
| `LogicSaveExt.IsMusicRoomUnlocked` | Prefix | Keep the Music Room visible |
| `ResourceManager.GetAllMusicID` | Postfix | Append custom tracks |
| `ResourceManager.GetMusicMeta` | Prefix | Custom meta + battle redirect |
| `ResourceManager.GetMusicClip` | Prefix | Custom clip (lazy decode) |
| `MusicRoomController.Display` | Postfix | Pre-decode clips |
| `MusicRoomController.Awake` | Postfix | Install Music Room config button and music pack button |
| `AddonsController.Awake` | Postfix | Install the "Music Pack" button |
| `LanguagePacksController.Awake` | Prefix | Skip the cloned controller when cloning the music pack page |
| `LanguagePacksController.OnButtonClickCallback` / `OnPackItemToggledCallback` | Prefix | Suppress the language pack controller on the music pack page |
| `LanguagePacksController.Display` | Prefix | Leave "music mode" when opening the language pack page |
| `MainManager.Initialize` | Postfix | Startup loading |
| `MusicManager.Play` / `SetPlayingMusic` | Prefix + Postfix | Battle/map BGM redirect |

### Platform notes

The target runs on Android's BepInEx CoreCLR, where JITting certain framework assemblies aborts immediately with `SIGABRT` (`libc++abi: terminating`). Avoid the following when developing:

- do not use `System.Text.Json` (use the bundled `MiniJson` instead);
- do not use CoreCLR `System.IO.Compression` (use the interop `Il2CppSystem.IO.Compression`);
- do not use `System.Uri` / `System.Private.Uri` (build `file://` strings manually);
- avoid `System.Linq` / `System.Globalization` where possible.

Others:

- Unity's `UnityWebRequestMultimedia` / `DownloadHandlerAudioClip` is unavailable in this build (the audio download icall is missing), so audio must be decoded manually;
- do not decode audio during startup (it produces silent AudioClips);
- when "cloning a UI instance for its appearance": a `TextMeshProUGUITranslator` on the text overwrites your text when enabled (disable it first); a cloned controller subscribes to events in `Awake` (skip it on first activation with a flag).

## Credits

- **MVZ2 (original game)** by Cuerzor — <https://github.com/Cuerzor/MinecraftVsZombies2Unity>
- **MVZ2Expand** by Tr-DESE107 — <https://github.com/Tr-DESE107/MinecraftVsZombies2_EXPAND>
- Framework: BepInEx.
- Decoders: NVorbis (Vorbis), Concentus (Opus).

## License

Copyright (C) 2026 Alhugh233.

This project is licensed under the **GNU General Public License v3.0**. The full license text is included verbatim in [LICENSE](LICENSE).

This project is **directly based on the two open-source games MVZ2 and MVZ2Expand** (both GPL-3.0). It is a runtime mod that interoperates with them through BepInEx / Harmony and does not redistribute the games' assemblies.

Third-party components (NVorbis, Concentus, Concentus.Oggfile) and their license notices are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
