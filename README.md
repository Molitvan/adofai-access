# ADOFAI Access

A blind accessibility mod for A Dance of Fire and Ice (work in progress)

<!-- website-hide-start -->

## View this on my website

ADOFAI Access is also available on my website (recommended for most users)

[View ADOFAI Access on my website](https://molitvan.me/projects/adofai-access)

<!-- website-hide-end -->

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
- Listen-repeat ([listen to a demo](https://audiopub.site/listen/4b236d95-298e-49cd-adca-70fcb7dc95f1)): Like Sequence Storm's Audio cues 1 mode. Breaks down the beats into 2 alternating types of groups, listen and repeat. While the listen group is active, it uses audio cues to play you the rhythm you'll need to execute in the next repeat group. You don't need to tap while a listen group is active. The number of beats in a group is configurable.
- Pattern preview ([listen to a demo](https://audiopub.site/listen/3d4c0417-3a48-4dbb-b070-0e15856aee17)): Like Sequence Storm's Audio cues 2 mode. Adds audio cues that let you listen to the rhythm you need to execute a configurable number of beats ahead of time

### Menu narration
Toggleable with F4

Controls:
- `Up` / `Down` / `Left` / `Right`: move
- `Home` / `End`: jump first/last (only works in certain menus)
- `Enter` / `Space`: activate
- `Escape`: close/Go back/Pause

### ADOFAI Access Settings menu (`F5` or `SQUARE/X`)
The mod's own settings menu

Options:
- Menu narration: turn menu narration on/off
- Play mode: change the play mode between vanilla, listen-repeat and pattern preview
- Pattern preview beats ahead: how many beats in advance should pattern preview play
- Pattern preview follow starting BPM: instead of the tap cues changing with the tempo of the song, everything just stays at the starting tempo (this can be useful in some levels to reduce confusion)
- Listen-repeat group beats: how long should a listen/repeat group be
- Listen-repeat follow starting BPM: instead of the listen/repeat group duration changing with the tempo of the level, it's always the same duration (can be useful in some levels to reduce confusion)
- Listen-repeat ducking: whether or not to audio duck the song while a listen group is active
- Listen-repeat start/end cue: whether the start/end cues should be by sound, speech, both or none
- Play cues in level preview: whether to play the mod'mod's tap cues in level preview

### Accessible menu (`F6` or `TRIANGLE/Y`)
Due to some of the game's menus like the main menu and the custom levels menu being themselves rhythm based and very hard to make accessible, the mod adds a custom linear menu accessible with F6 that allows access to everything that would normally be accessed in those rhythm based menus

### Level preview mode (`F8`)
Allows you to preview a level by automatically going through it and playing a sound cue on every tap. Using level preview automatically enables practice mode with no way to use it outside practice mode so previewing a level until the end doesn't count as completing it.

### Change play mode (`F9`)
Cycles between play modes (explained above)

### Debug level/runtime dump (`F7`)
Writes level/runtime JSON dumps to `UserData/ADOFAI_Access/LevelDumps`. Not useful to most users.

### Audio cue customization
While the mod ships with all the needed sounds, you can customize them by placing appropriately named files in `UserData/ADOFAI_Access/Audio`. The files have to be in the WAV format.
- `tap.wav`: the tap audio cue
- extra_tap.wav: the audio cue that plays alongside the tap audio cue in case of multitap e.g. in RJ-X
- `listen_start.wav`: the audio cue for the start of listen groups in listen-repeat
- `listen_end.wav`: the audio cue for the end of listen groups in listen-repeat
- `hold_start.wav`: the audio cue for the start of a hold
- `hold_end.wav`: the audio cue for the end of a hold
