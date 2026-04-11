using System;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class AccessSettingsMenu
    {
        private const KeyCode ToggleKey = KeyCode.F5;

        private sealed class SettingOption
        {
            public string Label;
            public string ControlType;
            public Func<ModSettingsData, string> GetValue;
            public Action<ModSettingsData, int> Change;
            public Action<ModSettingsData> Activate;
        }

        private static readonly SettingOption[] Options =
        {
            new SettingOption
            {
                Label = "Menu narration",
                ControlType = "toggle",
                GetValue = settings => settings.menuNarrationEnabled ? "on" : "off",
                Change = (settings, delta) => settings.menuNarrationEnabled = delta > 0,
                Activate = settings => settings.menuNarrationEnabled = !settings.menuNarrationEnabled
            },
            new SettingOption
            {
                Label = "Play mode",
                ControlType = "option",
                GetValue = settings => PatternPreview.GetModeLabel(settings.playMode),
                Change = (_, delta) => PatternPreview.StepMode(delta, speak: false),
                Activate = _ => PatternPreview.StepMode(1, speak: false)
            },
            new SettingOption
            {
                Label = "Pattern preview beats ahead",
                ControlType = "setting",
                GetValue = settings => settings.patternPreviewBeatsAhead.ToString(),
                Change = (settings, delta) => settings.patternPreviewBeatsAhead = StepBeatSetting(settings.patternPreviewBeatsAhead, delta),
                Activate = settings => settings.patternPreviewBeatsAhead = WrapBeatSetting(settings.patternPreviewBeatsAhead)
            },
            new SettingOption
            {
                Label = "Listen-repeat group beats",
                ControlType = "setting",
                GetValue = settings => settings.listenRepeatGroupBeats.ToString(),
                Change = (settings, delta) => settings.listenRepeatGroupBeats = StepBeatSetting(settings.listenRepeatGroupBeats, delta),
                Activate = settings => settings.listenRepeatGroupBeats = WrapBeatSetting(settings.listenRepeatGroupBeats)
            },
            new SettingOption
            {
                Label = "Listen-repeat ducking",
                ControlType = "toggle",
                GetValue = settings => settings.listenRepeatAudioDuckingEnabled ? "on" : "off",
                Change = (settings, delta) => settings.listenRepeatAudioDuckingEnabled = delta > 0,
                Activate = settings => settings.listenRepeatAudioDuckingEnabled = !settings.listenRepeatAudioDuckingEnabled
            },
            new SettingOption
            {
                Label = "Listen-repeat start/end cue",
                ControlType = "option",
                GetValue = settings => GetCueModeLabel(settings.listenRepeatStartEndCueMode),
                Change = (settings, delta) => settings.listenRepeatStartEndCueMode = GetNextCueMode(settings.listenRepeatStartEndCueMode, delta),
                Activate = settings => settings.listenRepeatStartEndCueMode = GetNextCueMode(settings.listenRepeatStartEndCueMode, 1)
            },
            new SettingOption
            {
                Label = "ADOFAI Access version",
                ControlType = "button",
                GetValue = _ => Core.VersionString,
                Change = null,
                Activate = null
            }
        };

        private static bool _open;
        private static int _selectedIndex;
        private static bool _restoreResponsive;
        private static bool _wasPausedBeforeOpen;

        public static bool IsOpen => _open;
        public static void CloseFromExternal(bool speak = false) => Close(speak);

        public static void Tick()
        {
            if (Input.GetKeyDown(ToggleKey))
            {
                if (_open)
                {
                    Close();
                }
                else
                {
                    Open();
                }
                return;
            }

            if (!_open)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CustomMenuInputGuard.SuppressPauseForFrames();
                Close();
                return;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                _selectedIndex = (_selectedIndex + Options.Length - 1) % Options.Length;
                SpeakCurrentOption();
                return;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                _selectedIndex = (_selectedIndex + 1) % Options.Length;
                SpeakCurrentOption();
                return;
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                ChangeCurrentOption(-1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                ChangeCurrentOption(1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            {
                ToggleCurrentOption();
            }
        }

        private static void Open()
        {
            if (AccessibleLevelSelectMenu.IsOpen)
            {
                AccessibleLevelSelectMenu.CloseFromExternal(speak: false);
            }

            _open = true;
            _selectedIndex = 0;

            scrController controller = ADOBase.controller;
            if (controller != null)
            {
                _restoreResponsive = controller.responsive;
                controller.responsive = false;
                _wasPausedBeforeOpen = controller.paused;
                if (controller.paused)
                {
                    controller.TogglePauseGame();
                }
            }
            else
            {
                _restoreResponsive = false;
                _wasPausedBeforeOpen = false;
            }

            MenuNarration.Speak("ADOFAI Access settings. Up and down to navigate. Left and right to change. Escape to close.", interrupt: true);
            SpeakCurrentOption();
        }

        private static void Close(bool speak = true)
        {
            _open = false;

            scrController controller = ADOBase.controller;
            if (controller != null)
            {
                controller.responsive = _restoreResponsive;

                if (_wasPausedBeforeOpen && !controller.paused)
                {
                    controller.TogglePauseGame();
                }
            }

            _restoreResponsive = false;
            _wasPausedBeforeOpen = false;
            if (speak)
            {
                MenuNarration.Speak("ADOFAI Access settings closed", interrupt: true);
            }
        }

        private static void ChangeCurrentOption(int delta)
        {
            SettingOption option = Options[_selectedIndex];
            if (option.Change == null)
            {
                SpeakCurrentValue();
                return;
            }

            ModSettingsData settings = ModSettings.Current;
            option.Change(settings, delta);
            ModSettings.Save();
            SpeakChangedValue(settings);
        }

        private static void ToggleCurrentOption()
        {
            SettingOption option = Options[_selectedIndex];
            if (option.Activate == null)
            {
                SpeakCurrentValue();
                return;
            }

            ModSettingsData settings = ModSettings.Current;
            option.Activate(settings);
            ModSettings.Save();
            SpeakChangedValue(settings);
        }

        private static void SpeakCurrentOption()
        {
            ModSettingsData settings = ModSettings.Current;
            SettingOption option = Options[_selectedIndex];
            string value = option.GetValue != null ? option.GetValue(settings) : string.Empty;
            MenuNarration.Speak(
                $"{option.Label}, {value}, {option.ControlType}, {_selectedIndex + 1} of {Options.Length}",
                interrupt: true);
        }

        private static void SpeakCurrentValue()
        {
            ModSettingsData settings = ModSettings.Current;
            SettingOption option = Options[_selectedIndex];
            string value = option.GetValue != null ? option.GetValue(settings) : string.Empty;
            SpeakAlways(value, true);
        }

        private static void SpeakChangedValue(ModSettingsData settings)
        {
            if (_selectedIndex == 0 && !settings.menuNarrationEnabled)
            {
                SpeakAlways("Menu narration off. You can always turn menu narration back on with F4.", true);
                return;
            }

            SettingOption option = Options[_selectedIndex];
            string value = option.GetValue != null ? option.GetValue(settings) : string.Empty;
            SpeakAlways(value, true);
        }

        private static int StepBeatSetting(int currentValue, int delta)
        {
            int next = currentValue + delta;
            if (next < 1)
            {
                return 1;
            }

            if (next > 16)
            {
                return 16;
            }

            return next;
        }

        private static int WrapBeatSetting(int currentValue)
        {
            return currentValue >= 16 ? 1 : currentValue + 1;
        }

        private static ListenRepeatStartEndCueMode GetNextCueMode(ListenRepeatStartEndCueMode current, int delta)
        {
            ListenRepeatStartEndCueMode[] cycle =
            {
                ListenRepeatStartEndCueMode.Sound,
                ListenRepeatStartEndCueMode.Speech,
                ListenRepeatStartEndCueMode.Both,
                ListenRepeatStartEndCueMode.None
            };

            int index = 0;
            for (int i = 0; i < cycle.Length; i++)
            {
                if (cycle[i] == current)
                {
                    index = i;
                    break;
                }
            }

            if (delta == 0)
            {
                return cycle[index];
            }

            int next = index + (delta > 0 ? 1 : -1);
            if (next < 0)
            {
                next = cycle.Length - 1;
            }
            else if (next >= cycle.Length)
            {
                next = 0;
            }

            return cycle[next];
        }

        private static string GetCueModeLabel(ListenRepeatStartEndCueMode mode)
        {
            switch (mode)
            {
                case ListenRepeatStartEndCueMode.Speech:
                    return "speech";
                case ListenRepeatStartEndCueMode.Both:
                    return "both";
                case ListenRepeatStartEndCueMode.None:
                    return "none";
                default:
                    return "sound";
            }
        }

        private static void SpeakAlways(string text, bool interrupt)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (ModSettings.Current.menuNarrationEnabled)
            {
                MenuNarration.Speak(text, interrupt);
            }
            else
            {
                PrismSpeech.Output(text, interrupt);
            }
        }
    }
}
