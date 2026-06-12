using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class LevelPreview
    {
        private const KeyCode ToggleKey = KeyCode.F8;
        private const KeyCode AlternateToggleKey = KeyCode.P;
        private const double CueLateGraceSeconds = 0.04;

        private static bool _active;
        private static bool _previousAuto;
        private static bool _previousPracticeMode;
        private static bool _previousSpeedTrialMode;
        private static float _previousSpeedTrialValue;
        private static int _previousCheckpointNum;
        private static int _previousPracticeLength;

        private static bool _toggleHintSpoken;
        private static readonly HashSet<int> HandledSeqIds = new HashSet<int>();
        private static List<scrFloor> _handledFloorList;
        private static int _handledFloorCount;

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
            TryScheduleDueCues();
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

        private static void ResetCueTracking()
        {
            HandledSeqIds.Clear();
            _handledFloorList = null;
            _handledFloorCount = 0;
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
            _previousCheckpointNum = GCS.checkpointNum;
            _previousPracticeLength = GCS.practiceLength;

            int floorCount = ADOBase.lm != null && ADOBase.lm.listFloors != null ? ADOBase.lm.listFloors.Count : 0;
            int checkpoint = Mathf.Clamp(GCS.checkpointNum, 0, Mathf.Max(0, floorCount - 1));
            int remainingLength = floorCount > 0 ? Mathf.Max(0, floorCount - 1 - checkpoint) : GCS.practiceLength;

            _active = true;
            GCS.practiceMode = true;
            GCS.checkpointNum = checkpoint;
            GCS.practiceLength = remainingLength;
            GCS.speedTrialMode = false;
            RDC.auto = true;
            ResetCueTracking();

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
            GCS.checkpointNum = _previousCheckpointNum;
            GCS.practiceLength = _previousPracticeLength;
            GCS.speedTrialMode = _previousSpeedTrialMode;
            GCS.currentSpeedTrial = _previousSpeedTrialValue;
            GCS.nextSpeedRun = _previousSpeedTrialValue;
            ResetCueTracking();

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

            if (ADOBase.sceneName == GCNS.sceneCustomLevelSelect || ADOBase.cls != null)
            {
                return false;
            }

            if (ADOBase.sceneName == GCNS.sceneGame || ADOBase.isScnGame || ADOBase.isPlayingLevel)
            {
                return true;
            }

            return controller.gameworld || controller.isPuzzleRoom;
        }

        private static void TryScheduleDueCues()
        {
            // Preview auto-play still runs; only the cue audio is suppressed when this option is off.
            if (!ModSettings.Current.levelPreviewCuesEnabled)
            {
                return;
            }

            scrController controller = ADOBase.controller;
            scrConductor conductor = ADOBase.conductor;
            scrLevelMaker levelMaker = ADOBase.lm;
            if (controller == null || conductor == null || levelMaker == null || levelMaker.listFloors == null)
            {
                return;
            }

            if (controller.paused || controller.state != States.PlayerControl)
            {
                return;
            }

            List<scrFloor> floors = levelMaker.listFloors;

            // A rebuilt floor list (restart/checkpoint reload) means seqIDs are fresh; drop stale handled state.
            if (!ReferenceEquals(floors, _handledFloorList) || floors.Count != _handledFloorCount)
            {
                HandledSeqIds.Clear();
                _handledFloorList = floors;
                _handledFloorCount = floors.Count;
            }

            double nowDsp = conductor.dspTime;
            double horizon = PlayModeTiming.GetScheduleHorizonSeconds(conductor);

            // Taps correspond to entering each floor *after* the one the planet currently sits on;
            // the start tile itself is not a tap. seqID is path order, so currFloor.seqID is the cutoff.
            scrFloor currentFloor = controller.currFloor;
            int currentSeqID = currentFloor != null ? currentFloor.seqID : int.MinValue;

            // Schedule every upcoming tap within the horizon (not just currFloor.nextfloor) so that
            // tightly-clustered taps are all cued ahead of time instead of being dropped as "late"
            // by the time the auto-player walks onto each floor one hop per frame.
            for (int i = 0; i < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                if (floor == null || floor.auto || floor.seqID <= currentSeqID)
                {
                    continue;
                }

                if (HandledSeqIds.Contains(floor.seqID))
                {
                    continue;
                }

                // Perfect-center timing: cue lands exactly on the tile's entry time (no lead).
                double dueDsp = conductor.dspTimeSongPosZero + floor.entryTimePitchAdj;
                double untilDue = dueDsp - nowDsp;
                if (untilDue > horizon)
                {
                    continue;
                }

                HandledSeqIds.Add(floor.seqID);
                if (untilDue >= 0.0)
                {
                    TapCueService.PlayCueAt(dueDsp);
                }
                else if (untilDue >= -CueLateGraceSeconds)
                {
                    // Slightly past center due to frame timing: play immediately as late-grace fallback.
                    TapCueService.PlayCueNow();
                }
            }
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
