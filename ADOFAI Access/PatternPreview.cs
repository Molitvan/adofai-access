using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class PatternPreview
    {
        private const KeyCode ToggleKey = KeyCode.F9;
        private const double CueScheduleHorizonSeconds = 0.25;
        private const double CueLateGraceSeconds = 0.04;
        private const float ListenPhaseDuckFactor = 0.35f;

        private static bool _toggleHintSpoken;
        private static bool _runtimeModeActive;
        private static PlayMode _runtimeMode = PlayMode.Vanilla;
        private static bool _hasStoredPreviousAuto;
        private static bool _previousAuto;
        private static int _listenRepeatPhase = -1; // 0 = Listen, 1 = Repeat
        private static bool _isListenDuckingActive;
        private static float _preDuckSongVolume = 1f;
        private static readonly HashSet<int> HandledSeqIds = new HashSet<int>();
        private static readonly HashSet<string> HandledListenBoundaryCueKeys = new HashSet<string>();

        public static bool IsActive => ModSettings.Current.playMode != PlayMode.Vanilla;
        public static PlayMode CurrentMode => ModSettings.Current.playMode;
        public static string ToggleHint => "Press F9 to cycle play mode";

        public static void Tick()
        {
            bool inGameplay = IsGameplayRuntimeAvailable();
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

            if (controller.paused || !CanScheduleInCurrentState(controller))
            {
                ResetSchedulingState();
                TapCueService.StopAllCues();
                if (_runtimeMode == PlayMode.ListenRepeat)
                {
                    RestoreAuto();
                    ApplyListenDucking(ADOBase.conductor, shouldDuck: false);
                }
                return;
            }

            switch (_runtimeMode)
            {
                case PlayMode.PatternPreview:
                    RestoreAuto();
                    ApplyListenDucking(ADOBase.conductor, shouldDuck: false);
                    TryScheduleNextBarCues();
                    break;
                case PlayMode.ListenRepeat:
                    TickListenRepeat(controller);
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
                _listenRepeatPhase = -1;
                ResetSchedulingState();
                MelonLogger.Msg($"[ADOFAI Access] Play mode runtime active: {GetModeLabel(mode)}.");
                return;
            }

            if (_runtimeMode == mode)
            {
                return;
            }

            RestoreAuto();
            ApplyListenDucking(ADOBase.conductor, shouldDuck: false);
            TapCueService.StopAllCues();
            _runtimeMode = mode;
            _listenRepeatPhase = -1;
            ResetSchedulingState();
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
            ApplyListenDucking(ADOBase.conductor, shouldDuck: false);
            _runtimeMode = PlayMode.Vanilla;
            _listenRepeatPhase = -1;
            ResetSchedulingState();
        }

        private static void RestoreAuto()
        {
            if (!_hasStoredPreviousAuto)
            {
                return;
            }

            RDC.auto = _previousAuto;
        }

        private static void ApplyListenDucking(scrConductor conductor, bool shouldDuck)
        {
            shouldDuck = shouldDuck && ModSettings.Current.listenRepeatAudioDuckingEnabled;

            if (conductor == null || conductor.song == null)
            {
                _isListenDuckingActive = false;
                return;
            }

            if (shouldDuck)
            {
                if (!_isListenDuckingActive)
                {
                    _preDuckSongVolume = conductor.song.volume;
                    _isListenDuckingActive = true;
                }

                conductor.song.volume = Mathf.Clamp01(_preDuckSongVolume * ListenPhaseDuckFactor);
                return;
            }

            if (_isListenDuckingActive)
            {
                conductor.song.volume = Mathf.Clamp01(_preDuckSongVolume);
                _isListenDuckingActive = false;
            }
        }

        private static void TickListenRepeat(scrController controller)
        {
            scrConductor conductor = ADOBase.conductor;
            if (conductor == null)
            {
                return;
            }

            if (controller.state != States.PlayerControl)
            {
                // Do not emit listen-repeat cues during countdown/checkpoint phases.
                TapCueService.StopAllCues();
                ApplyListenDucking(conductor, shouldDuck: false);
                return;
            }

            if (!TryGetCurrentBeat(controller, conductor, out double currentBeat))
            {
                ApplyListenDucking(conductor, shouldDuck: false);
                return;
            }

            int beatsPerGroup = Math.Max(1, ModSettings.Current.patternPreviewBeatsAhead);
            int groupIndex = Mathf.FloorToInt((float)(currentBeat / beatsPerGroup));
            TryScheduleListenBoundaryCues(conductor, beatsPerGroup, groupIndex);
            int phase = (groupIndex & 1) == 0 ? 0 : 1;
            bool phaseChanged = phase != _listenRepeatPhase;
            if (phaseChanged)
            {
                _listenRepeatPhase = phase;
                ResetSchedulingState();
                TapCueService.StopAllCues();
                ListenRepeatStartEndCueMode cueMode = ModSettings.Current.listenRepeatStartEndCueMode;
                bool useSpeech = cueMode == ListenRepeatStartEndCueMode.Speech || cueMode == ListenRepeatStartEndCueMode.Both;
                if (useSpeech)
                {
                    MenuNarration.Speak(phase == 0 ? "Listen" : "Repeat", interrupt: true);
                }
            }

            if (phase == 0)
            {
                RDC.auto = true;
                ApplyListenDucking(conductor, shouldDuck: true);
                TryScheduleListenRepeatGroupCues(conductor, groupIndex, beatsPerGroup);
                return;
            }

            // Edge case: if a repeat target was never cued during the listen phase
            // (for example near level end), temporarily automate that target.
            RDC.auto = ShouldForceAutoForUncuedRepeatTarget(controller);
            ApplyListenDucking(conductor, shouldDuck: false);
        }

        private static bool ShouldForceAutoForUncuedRepeatTarget(scrController controller)
        {
            if (controller == null)
            {
                return false;
            }

            scrFloor current = controller.currFloor;
            scrFloor target = current != null ? current.nextfloor : null;
            if (target == null || target.auto)
            {
                return false;
            }

            if (HandledSeqIds.Contains(target.seqID))
            {
                return false;
            }

            return IsLastRequiredFloor(target);
        }

        private static bool IsLastRequiredFloor(scrFloor target)
        {
            if (target == null)
            {
                return false;
            }

            List<scrFloor> floors = ADOBase.lm != null ? ADOBase.lm.listFloors : null;
            if (floors == null || floors.Count == 0)
            {
                return false;
            }

            bool seenTarget = false;
            for (int i = 0; i < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                if (floor == null)
                {
                    continue;
                }

                if (!seenTarget)
                {
                    if (ReferenceEquals(floor, target) || floor.seqID == target.seqID)
                    {
                        seenTarget = true;
                    }

                    continue;
                }

                if (!floor.auto)
                {
                    return false;
                }
            }

            return seenTarget;
        }

        private static void TryScheduleListenBoundaryCues(scrConductor conductor, int beatsPerGroup, int currentGroupIndex)
        {
            scrLevelMaker levelMaker = ADOBase.lm;
            if (conductor == null || levelMaker == null || levelMaker.listFloors == null || beatsPerGroup <= 0)
            {
                return;
            }

            List<scrFloor> floors = levelMaker.listFloors;
            int minGroup = currentGroupIndex - 2;
            int maxGroup = currentGroupIndex + 2;
            for (int groupIndex = minGroup; groupIndex <= maxGroup; groupIndex++)
            {
                if ((groupIndex & 1) != 0)
                {
                    continue;
                }

                double listenStartBeat = groupIndex * (double)beatsPerGroup;
                double listenEndBeat = listenStartBeat + beatsPerGroup;
                TryScheduleListenBoundaryCue(conductor, floors, groupIndex, "start", listenStartBeat, TapCueService.GetListenStartCueDurationSeconds(), TapCueService.PlayListenStartAt, TapCueService.PlayListenStartNow);
                TryScheduleListenBoundaryCue(conductor, floors, groupIndex, "end", listenEndBeat, TapCueService.GetListenEndCueDurationSeconds(), TapCueService.PlayListenEndAt, TapCueService.PlayListenEndNow);
            }
        }

        private static void TryScheduleListenBoundaryCue(
            scrConductor conductor,
            List<scrFloor> floors,
            int listenGroupIndex,
            string markerType,
            double markerBeat,
            double cueDurationSeconds,
            Action<double> scheduleAction,
            Action immediateAction)
        {
            string key = listenGroupIndex.ToString() + ":" + markerType;
            if (HandledListenBoundaryCueKeys.Contains(key))
            {
                return;
            }

            if (!TryGetCueDspForBeat(conductor, floors, markerBeat, out double boundaryDsp))
            {
                return;
            }

            double cueStartDsp = boundaryDsp - Math.Max(0.0, cueDurationSeconds);
            double untilCueStart = cueStartDsp - conductor.dspTime;
            if (untilCueStart < -CueLateGraceSeconds)
            {
                HandledListenBoundaryCueKeys.Add(key);
                return;
            }

            if (untilCueStart > CueScheduleHorizonSeconds)
            {
                return;
            }

            ListenRepeatStartEndCueMode cueMode = ModSettings.Current.listenRepeatStartEndCueMode;
            bool useSound = cueMode == ListenRepeatStartEndCueMode.Sound || cueMode == ListenRepeatStartEndCueMode.Both;

            HandledListenBoundaryCueKeys.Add(key);
            if (useSound)
            {
                if (untilCueStart >= 0.0)
                {
                    scheduleAction(cueStartDsp);
                }
                else
                {
                    immediateAction();
                }
            }

        }

        private static void TryScheduleListenRepeatGroupCues(scrConductor conductor, int listenGroupIndex, int beatsPerGroup)
        {
            scrLevelMaker levelMaker = ADOBase.lm;
            if (conductor == null || levelMaker == null || levelMaker.listFloors == null || beatsPerGroup <= 0)
            {
                return;
            }

            List<scrFloor> floors = levelMaker.listFloors;
            int repeatGroupIndex = listenGroupIndex + 1;
            double listenStartBeat = listenGroupIndex * (double)beatsPerGroup;
            double repeatStartBeat = repeatGroupIndex * (double)beatsPerGroup;
            double repeatEndBeat = repeatStartBeat + beatsPerGroup;

            if (!TryGetEntryTimePitchAdjustedForBeat(floors, listenStartBeat, out double listenStartTime))
            {
                return;
            }

            if (!TryGetEntryTimePitchAdjustedForBeat(floors, repeatStartBeat, out double repeatStartTime))
            {
                return;
            }

            // Shift the upcoming repeat group back to the listen group's start.
            // This preserves the repeat group's internal timing through tempo changes.
            double groupTimeShift = repeatStartTime - listenStartTime;
            double nowDsp = conductor.dspTime;

            for (int i = 0; i < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                if (floor == null || floor.auto)
                {
                    continue;
                }

                if (HandledSeqIds.Contains(floor.seqID))
                {
                    continue;
                }

                if (floor.entryBeat < repeatStartBeat || floor.entryBeat >= repeatEndBeat)
                {
                    continue;
                }

                double previewDueDsp = conductor.dspTimeSongPosZero + floor.entryTimePitchAdj - groupTimeShift;
                double untilPreview = previewDueDsp - nowDsp;
                if (untilPreview < -CueLateGraceSeconds)
                {
                    HandledSeqIds.Add(floor.seqID);
                    continue;
                }

                if (untilPreview > CueScheduleHorizonSeconds)
                {
                    continue;
                }

                HandledSeqIds.Add(floor.seqID);
                if (untilPreview >= 0.0)
                {
                    TapCueService.PlayCueAt(previewDueDsp);
                }
                else
                {
                    TapCueService.PlayCueNow();
                }
            }
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

        private static void TryScheduleNextBarCues()
        {
            scrConductor conductor = ADOBase.conductor;
            scrLevelMaker levelMaker = ADOBase.lm;
            if (conductor == null || levelMaker == null || levelMaker.listFloors == null)
            {
                return;
            }

            double nowDsp = conductor.dspTime;
            List<scrFloor> floors = levelMaker.listFloors;
            for (int i = 0; i < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                if (floor == null || floor.auto)
                {
                    continue;
                }

                if (HandledSeqIds.Contains(floor.seqID))
                {
                    continue;
                }

                if (!TryGetPreviewCueDsp(conductor, floors, floor, out double previewDueDsp))
                {
                    continue;
                }

                double untilPreview = previewDueDsp - nowDsp;
                if (untilPreview < -CueLateGraceSeconds)
                {
                    HandledSeqIds.Add(floor.seqID);
                    continue;
                }

                if (untilPreview > CueScheduleHorizonSeconds)
                {
                    continue;
                }

                HandledSeqIds.Add(floor.seqID);
                if (untilPreview >= 0.0)
                {
                    TapCueService.PlayCueAt(previewDueDsp);
                }
                else
                {
                    TapCueService.PlayCueNow();
                }
            }
        }

        private static bool TryGetCurrentBeat(scrController controller, scrConductor conductor, out double currentBeat)
        {
            currentBeat = 0.0;

            if (controller.state == States.Countdown || controller.state == States.Checkpoint)
            {
                if (conductor.crotchetAtStart > 0f)
                {
                    currentBeat = conductor.songposition_minusi / conductor.crotchetAtStart;
                }
                else
                {
                    currentBeat = conductor.beatNumber - conductor.adjustedCountdownTicks;
                }
                return true;
            }

            scrFloor current = controller.currFloor;
            if (current == null)
            {
                return false;
            }

            scrFloor next = current.nextfloor;
            if (next == null)
            {
                currentBeat = current.entryBeat;
                return true;
            }

            double t0 = current.entryTimePitchAdj;
            double t1 = next.entryTimePitchAdj;
            double b0 = current.entryBeat;
            double b1 = next.entryBeat;
            if (t1 <= t0 || b1 < b0)
            {
                currentBeat = b0;
                return true;
            }

            double nowT = conductor.dspTime - conductor.dspTimeSongPosZero;
            double alpha = (nowT - t0) / (t1 - t0);
            if (alpha < 0.0)
            {
                alpha = 0.0;
            }
            else if (alpha > 1.0)
            {
                alpha = 1.0;
            }

            currentBeat = b0 + (b1 - b0) * alpha;
            return true;
        }

        private static bool TryGetPreviewCueDsp(scrConductor conductor, List<scrFloor> floors, scrFloor targetFloor, out double previewDueDsp)
        {
            previewDueDsp = 0d;
            int beatsAhead = ModSettings.Current.patternPreviewBeatsAhead;
            if (beatsAhead <= 0)
            {
                return false;
            }

            double previewBeat = targetFloor.entryBeat - beatsAhead;
            if (previewBeat < 0.0)
            {
                float pitchEarly = conductor.song != null && conductor.song.pitch > 0f ? conductor.song.pitch : 1f;
                double earlyLeadSeconds = conductor.crotchetAtStart * beatsAhead / pitchEarly;
                previewDueDsp = conductor.dspTimeSongPosZero + targetFloor.entryTimePitchAdj - earlyLeadSeconds;
                return true;
            }

            if (TryGetEntryTimePitchAdjustedForBeat(floors, previewBeat, out double previewEntryTime))
            {
                previewDueDsp = conductor.dspTimeSongPosZero + previewEntryTime;
                return true;
            }

            float pitch = conductor.song != null && conductor.song.pitch > 0f ? conductor.song.pitch : 1f;
            double leadSeconds = conductor.crotchetAtStart * beatsAhead / pitch;
            previewDueDsp = conductor.dspTimeSongPosZero + targetFloor.entryTimePitchAdj - leadSeconds;
            return true;
        }

        private static bool TryGetCueDspForBeat(scrConductor conductor, List<scrFloor> floors, double beat, out double cueDsp)
        {
            cueDsp = 0d;
            if (conductor == null)
            {
                return false;
            }

            if (TryGetEntryTimePitchAdjustedForBeat(floors, beat, out double entryTimePitchAdj))
            {
                cueDsp = conductor.dspTimeSongPosZero + entryTimePitchAdj;
                return true;
            }

            float pitch = conductor.song != null && conductor.song.pitch > 0f ? conductor.song.pitch : 1f;
            cueDsp = conductor.dspTimeSongPosZero + (conductor.crotchetAtStart * beat / pitch);
            return true;
        }

        private static bool TryGetEntryTimePitchAdjustedForBeat(List<scrFloor> floors, double beat, out double entryTimePitchAdj)
        {
            entryTimePitchAdj = 0d;
            if (floors == null || floors.Count == 0)
            {
                return false;
            }

            scrFloor firstA = null;
            scrFloor firstB = null;
            scrFloor lastA = null;
            scrFloor lastB = null;

            for (int i = 1; i < floors.Count; i++)
            {
                scrFloor a = floors[i - 1];
                scrFloor b = floors[i];
                if (a == null || b == null)
                {
                    continue;
                }

                if (b.entryBeat <= a.entryBeat)
                {
                    continue;
                }

                if (firstA == null)
                {
                    firstA = a;
                    firstB = b;
                }

                lastA = a;
                lastB = b;

                if (beat < a.entryBeat || beat > b.entryBeat)
                {
                    continue;
                }

                double alpha = (beat - a.entryBeat) / (b.entryBeat - a.entryBeat);
                entryTimePitchAdj = a.entryTimePitchAdj + (b.entryTimePitchAdj - a.entryTimePitchAdj) * alpha;
                return true;
            }

            if (firstA == null || firstB == null || lastA == null || lastB == null)
            {
                return false;
            }

            if (beat < firstA.entryBeat)
            {
                double slope = (firstB.entryTimePitchAdj - firstA.entryTimePitchAdj) / (firstB.entryBeat - firstA.entryBeat);
                entryTimePitchAdj = firstA.entryTimePitchAdj + (beat - firstA.entryBeat) * slope;
                return true;
            }

            if (beat > lastB.entryBeat)
            {
                double slope = (lastB.entryTimePitchAdj - lastA.entryTimePitchAdj) / (lastB.entryBeat - lastA.entryBeat);
                entryTimePitchAdj = lastB.entryTimePitchAdj + (beat - lastB.entryBeat) * slope;
                return true;
            }

            return false;
        }

        private static bool CanScheduleInCurrentState(scrController controller)
        {
            return controller.state == States.Countdown || controller.state == States.Checkpoint || controller.state == States.PlayerControl;
        }

        private static void ResetSchedulingState()
        {
            HandledSeqIds.Clear();
            HandledListenBoundaryCueKeys.Clear();
        }
    }
}
