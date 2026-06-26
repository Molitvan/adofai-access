using System;
using UnityEngine;

namespace ADOFAI_Access
{
    // Linear menu opened from the main-world F6 accessible menu. Lists each audio cue the mod
    // uses with a short description; activating an entry plays that cue so the user can learn it.
    internal static class AudioGlossaryMenu
    {
        private sealed class GlossaryEntry
        {
            public string Label;
            public string Description;
            public Action Play;
        }

        private static readonly GlossaryEntry[] Entries =
        {
            new GlossaryEntry
            {
                Label = "Tap",
                Description = "Plays on every tap.",
                Play = () => TapCueService.PlayCueNow()
            },
            new GlossaryEntry
            {
                Label = "Extra tap",
                Description = "Plays together with the tap on multitap tiles, where two keys must be pressed at once.",
                Play = () => TapCueService.PlayCueNow(multiTap: true)
            },
            new GlossaryEntry
            {
                Label = "Hold start",
                Description = "Plays when a hold begins.",
                Play = () => TapCueService.PlayHoldStartNow()
            },
            new GlossaryEntry
            {
                Label = "Hold end",
                Description = "Plays when a hold ends.",
                Play = () => TapCueService.PlayHoldEndNow()
            },
            new GlossaryEntry
            {
                Label = "Listen group start",
                Description = "In listen-repeat mode, marks the start of a listen group.",
                Play = () => TapCueService.PlayListenStartNow()
            },
            new GlossaryEntry
            {
                Label = "Listen group end",
                Description = "In listen-repeat mode, marks the end of a listen group.",
                Play = () => TapCueService.PlayListenEndNow()
            }
        };

        private static bool _open;
        private static int _selectedIndex;
        private static bool _restoreResponsive;

        public static bool IsOpen => _open;
        public static void CloseFromExternal(bool speak = false) => Close(speak);

        public static void Open()
        {
            if (AccessSettingsMenu.IsOpen)
            {
                AccessSettingsMenu.CloseFromExternal(speak: false);
            }

            if (AccessibleLevelSelectMenu.IsOpen)
            {
                AccessibleLevelSelectMenu.CloseFromExternal(speak: false);
            }

            _open = true;
            _selectedIndex = 0;

            scrController controller = ADOBase.controller;
            if (controller != null)
            {
                _restoreResponsive = ControllerCompat.GetResponsive();
                ControllerCompat.SetResponsive(false);
            }
            else
            {
                _restoreResponsive = false;
            }

            MenuNarration.Speak("ADOFAI Access audio cue glossary. Up and down to navigate. Enter to play the sound. Escape to close.", interrupt: true);
            SpeakSelection();
        }

        public static void Tick()
        {
            if (!_open)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) || MenuGamepad.CancelPressed())
            {
                CustomMenuInputGuard.SuppressPauseForFrames();
                Close(speak: true);
                return;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow) || MenuGamepad.UpPressed())
            {
                MoveSelection(-1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow) || MenuGamepad.DownPressed())
            {
                MoveSelection(1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Home))
            {
                _selectedIndex = 0;
                SpeakSelection();
                return;
            }

            if (Input.GetKeyDown(KeyCode.End))
            {
                _selectedIndex = Entries.Length - 1;
                SpeakSelection();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || MenuGamepad.ConfirmPressed())
            {
                Entries[_selectedIndex].Play?.Invoke();
            }
        }

        private static void Close(bool speak)
        {
            _open = false;
            TapCueService.StopAllCues();

            scrController controller = ADOBase.controller;
            if (controller != null)
            {
                ControllerCompat.SetResponsive(_restoreResponsive);
            }

            _restoreResponsive = false;

            if (speak)
            {
                MenuNarration.Speak("Audio cue glossary closed", interrupt: true);
            }
        }

        private static void MoveSelection(int delta)
        {
            _selectedIndex += delta;
            if (_selectedIndex < 0)
            {
                _selectedIndex = Entries.Length - 1;
            }
            else if (_selectedIndex >= Entries.Length)
            {
                _selectedIndex = 0;
            }

            SpeakSelection();
        }

        private static void SpeakSelection()
        {
            GlossaryEntry entry = Entries[_selectedIndex];
            MenuNarration.Speak($"{entry.Label}. {entry.Description} {_selectedIndex + 1} of {Entries.Length}", interrupt: true);
        }
    }
}
