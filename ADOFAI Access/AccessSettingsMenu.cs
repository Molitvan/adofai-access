using UnityEngine;
using DavyKager;

namespace ADOFAI_Access
{
    internal static class AccessSettingsMenu
    {
        private const KeyCode ToggleKey = KeyCode.F5;
        private const int OptionCount = 5;

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
                _selectedIndex = (_selectedIndex + OptionCount - 1) % OptionCount;
                SpeakCurrentOption();
                return;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                _selectedIndex = (_selectedIndex + 1) % OptionCount;
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
            ModSettingsData settings = ModSettings.Current;
            switch (_selectedIndex)
            {
                case 0:
                    settings.menuNarrationEnabled = delta > 0;
                    break;
                case 1:
                    PatternPreview.StepMode(delta, speak: false);
                    break;
                case 2:
                    settings.patternPreviewBeatsAhead += delta;
                    if (settings.patternPreviewBeatsAhead < 1)
                    {
                        settings.patternPreviewBeatsAhead = 1;
                    }
                    else if (settings.patternPreviewBeatsAhead > 16)
                    {
                        settings.patternPreviewBeatsAhead = 16;
                    }
                    break;
                case 3:
                    settings.listenRepeatAudioDuckingEnabled = delta > 0;
                    break;
                case 4:
                    settings.listenRepeatStartEndCueMode = GetNextCueMode(settings.listenRepeatStartEndCueMode, delta);
                    break;
            }

            ModSettings.Save();
            if (_selectedIndex == 0 && !settings.menuNarrationEnabled)
            {
                SpeakAlways("Menu narration off. You can always turn menu narration back on with F4.", true);
            }
            else
            {
                SpeakCurrentValue();
            }
        }

        private static void ToggleCurrentOption()
        {
            ModSettingsData settings = ModSettings.Current;
            switch (_selectedIndex)
            {
                case 0:
                    settings.menuNarrationEnabled = !settings.menuNarrationEnabled;
                    break;
                case 1:
                    PatternPreview.StepMode(1, speak: false);
                    break;
                case 2:
                    settings.patternPreviewBeatsAhead = settings.patternPreviewBeatsAhead >= 16 ? 1 : settings.patternPreviewBeatsAhead + 1;
                    break;
                case 3:
                    settings.listenRepeatAudioDuckingEnabled = !settings.listenRepeatAudioDuckingEnabled;
                    break;
                case 4:
                    settings.listenRepeatStartEndCueMode = GetNextCueMode(settings.listenRepeatStartEndCueMode, 1);
                    break;
            }

            ModSettings.Save();
            if (_selectedIndex == 0 && !settings.menuNarrationEnabled)
            {
                SpeakAlways("Menu narration off. You can always turn menu narration back on with F4.", true);
            }
            else
            {
                SpeakCurrentValue();
            }
        }

        private static void SpeakCurrentOption()
        {
            ModSettingsData settings = ModSettings.Current;
            switch (_selectedIndex)
            {
                case 0:
                    MenuNarration.Speak($"Menu narration, {(settings.menuNarrationEnabled ? "on" : "off")}, toggle, 1 of 5", interrupt: true);
                    break;
                case 1:
                    MenuNarration.Speak($"Play mode, {PatternPreview.GetModeLabel(settings.playMode)}, option, 2 of 5", interrupt: true);
                    break;
                case 2:
                    MenuNarration.Speak($"Pattern preview beats ahead, {settings.patternPreviewBeatsAhead}, setting, 3 of 5", interrupt: true);
                    break;
                case 3:
                    MenuNarration.Speak($"Listen-repeat ducking, {(settings.listenRepeatAudioDuckingEnabled ? "on" : "off")}, toggle, 4 of 5", interrupt: true);
                    break;
                case 4:
                    MenuNarration.Speak($"Listen-repeat start/end cue, {GetCueModeLabel(settings.listenRepeatStartEndCueMode)}, option, 5 of 5", interrupt: true);
                    break;
            }
        }

        private static void SpeakCurrentValue()
        {
            ModSettingsData settings = ModSettings.Current;
            switch (_selectedIndex)
            {
                case 0:
                    SpeakAlways(settings.menuNarrationEnabled ? "on" : "off", true);
                    break;
                case 1:
                    SpeakAlways(PatternPreview.GetModeLabel(settings.playMode), true);
                    break;
                case 2:
                    SpeakAlways(settings.patternPreviewBeatsAhead.ToString(), true);
                    break;
                case 3:
                    SpeakAlways(settings.listenRepeatAudioDuckingEnabled ? "on" : "off", true);
                    break;
                case 4:
                    SpeakAlways(GetCueModeLabel(settings.listenRepeatStartEndCueMode), true);
                    break;
            }
        }

        private static ListenRepeatStartEndCueMode GetNextCueMode(ListenRepeatStartEndCueMode current, int delta)
        {
            ListenRepeatStartEndCueMode[] cycle = new[]
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
                Tolk.Output(text, interrupt);
            }
        }
    }
}
