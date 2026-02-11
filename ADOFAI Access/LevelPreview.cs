using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class LevelPreview
    {
        private const KeyCode ToggleKey = KeyCode.F8;
        private const KeyCode AlternateToggleKey = KeyCode.P;
        private const double CueScheduleHorizonSeconds = 0.2;
        private const double CueLateGraceSeconds = 0.04;
        private const float CueMinIntervalSeconds = 0.03f;

        private static bool _active;
        private static bool _previousAuto;
        private static bool _previousPracticeMode;
        private static bool _previousSpeedTrialMode;
        private static float _previousSpeedTrialValue;

        private static bool _toggleHintSpoken;
        private static int _lastPredictedSeqId = -1;
        private static double _lastPredictedDueDsp = double.MinValue;
        private static float _lastCueAt;

        public static bool IsActive => _active;
        public static string CueFilePath => TapCueService.CueFilePath;
        public static string ToggleHint => "Press F8 to toggle level preview";

        public static void Tick()
        {
            bool inGameplay = IsGameplayRuntimeAvailable();
            if (!inGameplay)
            {
                _toggleHintSpoken = false;
            }
            else if (!_toggleHintSpoken)
            {
                _toggleHintSpoken = true;
                MenuNarration.Speak(ToggleHint, interrupt: false);
            }

            if (!AccessSettingsMenu.IsOpen && WasTogglePressed())
            {
                if (!Toggle())
                {
                    MenuNarration.Speak("Level preview unavailable here", interrupt: true);
                }
            }

            if (!_active)
            {
                return;
            }

            if (!IsGameplayRuntimeAvailable())
            {
                StopInternal(speak: false, restartLevel: false);
                return;
            }

            // Keep preview safety flags enforced while active.
            GCS.practiceMode = true;
            GCS.speedTrialMode = false;
            RDC.auto = true;
            TryPlayPredictedCue();
        }

        public static bool Toggle()
        {
            if (_active)
            {
                StopInternal(speak: true, restartLevel: true);
                return true;
            }

            return StartInternal();
        }

        public static void AnnouncePreviewComplete()
        {
            if (!_active)
            {
                return;
            }

            MenuNarration.Speak("Preview complete", interrupt: true);
            StopInternal(speak: false, restartLevel: false);

            scrController controller = ADOBase.controller;
            if (controller != null)
            {
                controller.Restart();
            }
        }

        public static void PlayTapCue()
        {
            if (!_active)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastCueAt < CueMinIntervalSeconds)
            {
                return;
            }

            TapCueService.PlayCueNow();
            _lastCueAt = now;
        }

        private static void PlayTapCueAt(double dspTime)
        {
            if (!_active)
            {
                return;
            }

            TapCueService.PlayCueAt(dspTime);
        }

        private static bool StartInternal()
        {
            if (_active)
            {
                return true;
            }

            if (!IsGameplayRuntimeAvailable())
            {
                MelonLogger.Msg("[ADOFAI Access] Level preview start rejected (not in gameplay).");
                return false;
            }

            _previousAuto = RDC.auto;
            _previousPracticeMode = GCS.practiceMode;
            _previousSpeedTrialMode = GCS.speedTrialMode;
            _previousSpeedTrialValue = GCS.currentSpeedTrial;

            _active = true;
            GCS.practiceMode = true;
            GCS.speedTrialMode = false;
            RDC.auto = true;
            _lastPredictedSeqId = -1;
            _lastPredictedDueDsp = double.MinValue;
            _lastCueAt = 0f;

            MelonLogger.Msg("[ADOFAI Access] Level preview enabled.");
            MenuNarration.Speak("Level preview on", interrupt: true);
            return true;
        }

        private static void StopInternal(bool speak, bool restartLevel)
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            TapCueService.StopAllCues();
            RDC.auto = _previousAuto;
            GCS.practiceMode = _previousPracticeMode;
            GCS.speedTrialMode = _previousSpeedTrialMode;
            GCS.currentSpeedTrial = _previousSpeedTrialValue;
            GCS.nextSpeedRun = _previousSpeedTrialValue;
            _lastPredictedSeqId = -1;
            _lastPredictedDueDsp = double.MinValue;

            if (speak)
            {
                MelonLogger.Msg("[ADOFAI Access] Level preview disabled.");
                MenuNarration.Speak("Level preview off", interrupt: true);
            }

            if (restartLevel)
            {
                scrController controller = ADOBase.controller;
                if (controller != null)
                {
                    controller.Restart();
                }
            }
        }

        private static bool WasTogglePressed()
        {
            if (Input.GetKeyDown(ToggleKey))
            {
                return true;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return ctrl && shift && Input.GetKeyDown(AlternateToggleKey);
        }

        private static bool IsGameplayRuntimeAvailable()
        {
            scrController controller = ADOBase.controller;
            if (controller == null || ADOBase.isLevelEditor)
            {
                return false;
            }

            if (ADOBase.sceneName == GCNS.sceneGame || ADOBase.isScnGame || ADOBase.isPlayingLevel)
            {
                return true;
            }

            return controller.gameworld || controller.isPuzzleRoom;
        }

        private static void TryPlayPredictedCue()
        {
            scrController controller = ADOBase.controller;
            scrConductor conductor = ADOBase.conductor;
            if (controller == null || conductor == null || controller.paused || controller.state != States.PlayerControl)
            {
                return;
            }

            scrFloor current = controller.currFloor;
            scrFloor target = current != null ? current.nextfloor : null;
            if (target == null || target.auto)
            {
                return;
            }

            double dueDsp = conductor.dspTimeSongPosZero + target.entryTimePitchAdj;
            double untilDue = dueDsp - conductor.dspTime;
            if (untilDue > CueScheduleHorizonSeconds || untilDue < -CueLateGraceSeconds)
            {
                return;
            }

            bool alreadyCued = target.seqID == _lastPredictedSeqId && Math.Abs(dueDsp - _lastPredictedDueDsp) < 0.001;
            if (alreadyCued)
            {
                return;
            }

            _lastPredictedSeqId = target.seqID;
            _lastPredictedDueDsp = dueDsp;
            if (untilDue >= 0.0)
            {
                // Perfect-center timing: schedule cue exactly at the tile's entry time.
                PlayTapCueAt(dueDsp);
                return;
            }

            // If slightly past center due to frame timing, play immediately as late-grace fallback.
            PlayTapCue();
        }

    }

    [HarmonyPatch(typeof(scrController), "Won_Enter")]
    internal static class LevelPreviewWonPatch
    {
        private static bool Prefix()
        {
            if (!LevelPreview.IsActive)
            {
                return true;
            }

            LevelPreview.AnnouncePreviewComplete();
            return false;
        }
    }
}
