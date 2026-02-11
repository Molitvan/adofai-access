using UnityEngine;

namespace ADOFAI_Access
{
    internal static class AccessSettingsMenu
    {
        private const KeyCode ToggleKey = KeyCode.F5;

        private static bool _open;
        private static int _selectedIndex;
        private static bool _restoreResponsive;
        private static bool _wasPausedBeforeOpen;

        public static bool IsOpen => _open;

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
                Close();
                return;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                _selectedIndex = (_selectedIndex + 2) % 3;
                SpeakCurrentOption();
                return;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                _selectedIndex = (_selectedIndex + 1) % 3;
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

        private static void Close()
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
            MenuNarration.Speak("ADOFAI Access settings closed", interrupt: true);
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
            }

            ModSettings.Save();
            SpeakCurrentOption();
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
            }

            ModSettings.Save();
            SpeakCurrentOption();
        }

        private static void SpeakCurrentOption()
        {
            ModSettingsData settings = ModSettings.Current;
            switch (_selectedIndex)
            {
                case 0:
                    MenuNarration.Speak($"Menu narration, {(settings.menuNarrationEnabled ? "on" : "off")}, toggle, 1 of 3", interrupt: true);
                    break;
                case 1:
                    MenuNarration.Speak($"Play mode, {PatternPreview.GetModeLabel(settings.playMode)}, option, 2 of 3", interrupt: true);
                    break;
                case 2:
                    MenuNarration.Speak($"Pattern preview beats ahead, {settings.patternPreviewBeatsAhead}, setting, 3 of 3", interrupt: true);
                    break;
            }
        }
    }
}
