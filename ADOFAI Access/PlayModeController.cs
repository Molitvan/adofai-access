using System;
using MelonLoader;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class PlayModeController
    {
        private const KeyCode ToggleKey = KeyCode.F9;

        private static bool _toggleHintSpoken;
        private static bool _runtimeModeActive;
        private static PlayMode _runtimeMode = PlayMode.Vanilla;
        private static bool _hasStoredPreviousAuto;
        private static bool _previousAuto;

        public static PlayMode CurrentMode => ModSettings.Current.playMode;
        public static string ToggleHint => "Press F9 to cycle play mode";

        public static void Tick()
        {
            bool inGameplay = PlayModeTiming.IsGameplayRuntimeAvailable();
            if (!inGameplay)
            {
                _toggleHintSpoken = false;
                StopRuntimeMode();
            }
            else if (!_toggleHintSpoken)
            {
                _toggleHintSpoken = true;
                MenuNarration.Speak(ToggleHint, interrupt: false);
            }

            if (!AccessSettingsMenu.IsOpen && Input.GetKeyDown(ToggleKey))
            {
                if (!inGameplay)
                {
                    MenuNarration.Speak("Play mode unavailable here", interrupt: true);
                }
                else
                {
                    CycleMode(speak: true);
                }
            }

            if (!inGameplay || LevelPreview.IsActive)
            {
                StopRuntimeMode();
                return;
            }

            PlayMode desiredMode = ModSettings.Current.playMode;
            if (desiredMode == PlayMode.Vanilla)
            {
                StopRuntimeMode();
                return;
            }

            EnsureRuntimeModeStarted(desiredMode);

            scrController controller = ADOBase.controller;
            if (controller == null)
            {
                return;
            }

            if (controller.paused || !PlayModeTiming.CanScheduleInCurrentState(controller))
            {
                TapCueService.StopAllCues();
                if (_runtimeMode == PlayMode.PatternPreview)
                {
                    PatternPreview.Stop();
                    RestoreAuto();
                }
                else if (_runtimeMode == PlayMode.ListenRepeat)
                {
                    ListenRepeatMode.Stop();
                    RestoreAuto();
                }
                return;
            }

            switch (_runtimeMode)
            {
                case PlayMode.PatternPreview:
                    RestoreAuto();
                    PatternPreview.Tick();
                    break;
                case PlayMode.ListenRepeat:
                    PatternPreview.Stop();
                    ListenRepeatMode.Tick(controller);
                    break;
            }
        }

        public static void CycleMode(bool speak)
        {
            SetMode(GetNextMode(CurrentMode), speak);
        }

        public static void StepMode(int delta, bool speak)
        {
            if (delta == 0)
            {
                return;
            }

            PlayMode mode = CurrentMode;
            int steps = Math.Abs(delta);
            for (int i = 0; i < steps; i++)
            {
                mode = delta > 0 ? GetNextMode(mode) : GetPreviousMode(mode);
            }

            SetMode(mode, speak);
        }

        public static void SetMode(PlayMode mode, bool speak)
        {
            if (!Enum.IsDefined(typeof(PlayMode), mode))
            {
                mode = PlayMode.Vanilla;
            }

            ModSettings.Current.playMode = mode;
            ModSettings.Save();

            if (mode != PlayMode.Vanilla && LevelPreview.IsActive)
            {
                LevelPreview.Toggle();
            }

            if (speak)
            {
                MenuNarration.Speak($"Play mode {GetModeLabel(mode)}", interrupt: true);
            }
        }

        public static string GetModeLabel(PlayMode mode)
        {
            switch (mode)
            {
                case PlayMode.PatternPreview:
                    return "pattern preview";
                case PlayMode.ListenRepeat:
                    return "listen-repeat";
                default:
                    return "vanilla";
            }
        }

        internal static void RestoreAuto()
        {
            if (!_hasStoredPreviousAuto)
            {
                return;
            }

            RDC.auto = _previousAuto;
        }

        private static PlayMode GetNextMode(PlayMode mode)
        {
            switch (mode)
            {
                case PlayMode.PatternPreview:
                    return PlayMode.ListenRepeat;
                case PlayMode.ListenRepeat:
                    return PlayMode.Vanilla;
                default:
                    return PlayMode.PatternPreview;
            }
        }

        private static PlayMode GetPreviousMode(PlayMode mode)
        {
            switch (mode)
            {
                case PlayMode.PatternPreview:
                    return PlayMode.Vanilla;
                case PlayMode.Vanilla:
                    return PlayMode.ListenRepeat;
                default:
                    return PlayMode.PatternPreview;
            }
        }

        private static void EnsureRuntimeModeStarted(PlayMode mode)
        {
            if (!_runtimeModeActive)
            {
                _runtimeModeActive = true;
                _runtimeMode = mode;
                _previousAuto = RDC.auto;
                _hasStoredPreviousAuto = true;
                PatternPreview.ResetForModeSwitch();
                ListenRepeatMode.ResetForModeSwitch();
                MelonLogger.Msg($"[ADOFAI Access] Play mode runtime active: {GetModeLabel(mode)}.");
                return;
            }

            if (_runtimeMode == mode)
            {
                return;
            }

            RestoreAuto();
            TapCueService.StopAllCues();
            _runtimeMode = mode;
            PatternPreview.ResetForModeSwitch();
            ListenRepeatMode.ResetForModeSwitch();
            MelonLogger.Msg($"[ADOFAI Access] Play mode runtime switched: {GetModeLabel(mode)}.");
        }

        private static void StopRuntimeMode()
        {
            if (!_runtimeModeActive)
            {
                return;
            }

            _runtimeModeActive = false;
            TapCueService.StopAllCues();
            RestoreAuto();
            _runtimeMode = PlayMode.Vanilla;
            PatternPreview.Stop();
            ListenRepeatMode.Stop();
        }
    }
}
