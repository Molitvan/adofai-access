# ADOFAI Access

A blind accessibility mod for A Dance of Fire and Ice (work in progress)

## Important note

This mod is still in early development. There are a lot of known issues and parts of the game that still aren't supported. Keep this in mind before purchasing the game. For any questions, the best place to ask is [the mod's channel on the Accessibility Disco Discord server](https://go.molitvan.me/ADOFAIAccessDiscord).

## Installation
Note: you have to have A Dance of Fire and Ice installed before following this
1. Download MelonLoader from https://melonwiki.xyz
2. Open the MelonLoader installer, select A Dance of Fire and Ice (or add it manually if it is not in the list), then install MelonLoader
3. [Download the latest release of ADOFAI Access](https://github.com/Molitvan/adofai-access/releases/latest)
4. Extract the contents of the downloaded ZIP into the game's root folder (the folder containing A Dance of Fire and Ice.exe).

## Main Features

### Listen-repeat and pattern preview play modes
This is the main feature of the mod that makes the gameplay accessible. There are 3 play modes you can choose from:
- Vanilla: plays like the original game, not modified by the mod at all
- Listen-repeat (like Sequence Storm's Audio cues 1 mode): breaks down the beats into 2 alternating types of groups, listen and repeat. While the listen group is active, it uses audio cues to play you the rhythm you'll need to execute in the next repeat group. You don't need to tap while a listen group is active. The number of beats in a group is configurable.
- Pattern preview (like Sequence Storm's Audio cues 2 mode): adds audio cues that let you listen to the rhythm you need to execute a configurable number of beats ahead of time

### Menu narration
Toggleable with F4

### ADOFAI Access Settings menu (`F5`)

Controls:
- `Up` / `Down`: move between settings
- `Left` / `Right`: change value
- `Enter` / `Space`: toggle
- `Escape`: close

### Accessible menu (`F6`)
Due to some of the game's menus like the main menu and the custom levels menu being themselves rhythm based and very hard to make accessible, the mod adds a custom linear menu accessible with F6 that allows access to everything that would normally be accessed in those rhythm based menus

Controls:
- `Up` / `Down`: move
- `Home` / `End`: jump first/last
- `Enter` / `Space`: activate
- `Escape`: close

### Level preview mode (`F8`)
Allows you to preview a level by automatically going through it and playing a sound cue on every tap. Using level preview automatically enables practice mode with no way to use it outside practice mode so previewing a level until the end doesn't count as completing it.

### Change play mode (`F9`)
Cycles between play modes (explained above)

### Debug level/runtime dump (`F7`)
Writes level/runtime JSON dumps to `UserData/ADOFAI_Access/LevelDumps`. Not useful to most users.

### Audio cue customization
While the mod ships with all the needed sounds, you can customize them by placing appropriately named files in `UserData/ADOFAI_Access/Audio`. The files have to be in the WAV format.
- `tap.wav`: the tap audio cue
- `listen_start.wav`: the audio cue for the start of listen groups in listen-repeat (is expected to be exactly 1 beat long at 128BPM)
- `listen_end.wav`: the audio cue for the end of listen groups in listen-repeat (is expected to be exactly 1 beat long at 128BPM)
