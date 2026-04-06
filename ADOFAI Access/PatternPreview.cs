using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class PatternPreview
    {
        private const KeyCode ToggleKey = KeyCode.F9;
        private const float ListenPhaseDuckFactor = 0.35f;
        private const double ListenRepeatBoundaryEpsilon = 0.0001d;

        private sealed class ListenCueEvent
        {
            public int SeqId;
            public double CueDsp;
        }

        private sealed class ArmedListenGroup
        {
            public int ListenGroupIndex;
            public int RepeatGroupIndex;
            public double ListenStartDsp;
            public double RepeatStartDsp;
            public readonly List<ListenCueEvent> Cues = new List<ListenCueEvent>();
            public readonly HashSet<int> ScheduledSeqIds = new HashSet<int>();
            public int NextCueIndex;
        }

        private static bool _toggleHintSpoken;
        private static bool _runtimeModeActive;
        private static PlayMode _runtimeMode = PlayMode.Vanilla;
        private static bool _hasStoredPreviousAuto;
        private static bool _previousAuto;
        private static int _listenRepeatPhase = -1; // 0 = Listen, 1 = Repeat
        private static bool _isListenDuckingActive;
        private static float _preDuckSongVolume = 1f;
        private static readonly HashSet<int> HandledSeqIds = new HashSet<int>();
        private static readonly HashSet<int> LastListenScheduledSeqIds = new HashSet<int>();
        private static readonly HashSet<string> HandledListenBoundaryCueKeys = new HashSet<string>();
        private static ArmedListenGroup _armedListenGroup;
        private static bool _listenRepeatStartupForced;
        private static bool _checkpointRecoveryActive;
        private static int _checkpointRecoveryTargetListenGroupIndex = -1;

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
                ResetCueSchedulingState();
                TapCueService.StopAllCues();
                ClearArmedListenGroup();
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
                LastListenScheduledSeqIds.Clear();
                ClearArmedListenGroup();
                _listenRepeatStartupForced = false;
                ResetCheckpointRecovery();
                ResetAllSchedulingState();
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
            LastListenScheduledSeqIds.Clear();
            ClearArmedListenGroup();
            _listenRepeatStartupForced = false;
            ResetCheckpointRecovery();
            ResetAllSchedulingState();
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
            LastListenScheduledSeqIds.Clear();
            ClearArmedListenGroup();
            _listenRepeatStartupForced = false;
            ResetCheckpointRecovery();
            ResetAllSchedulingState();
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
                ResetListenRepeatRunState();
                return;
            }

            if (!TryGetCurrentBeat(controller, conductor, out double currentBeat))
            {
                ApplyListenDucking(conductor, shouldDuck: false);
                return;
            }

            double nowDsp = conductor.dspTime;
            int beatsPerGroup = Math.Max(1, ModSettings.Current.patternPreviewBeatsAhead);
            int groupIndex = Mathf.FloorToInt((float)(currentBeat / beatsPerGroup));
            int phase = (groupIndex & 1) == 0 ? 0 : 1;
            if (HandleCheckpointRecovery(conductor, currentBeat, beatsPerGroup))
            {
                return;
            }

            bool suppressStartupListenAnnouncement = false;
            if (_listenRepeatStartupForced)
            {
                if (currentBeat < beatsPerGroup)
                {
                    groupIndex = 0;
                    phase = 0;
                    suppressStartupListenAnnouncement = true;
                }
                else
                {
                    _listenRepeatStartupForced = false;
                }
            }

            bool phaseChanged = phase != _listenRepeatPhase;
            if (phaseChanged)
            {
                if (_listenRepeatPhase == 0)
                {
                    CaptureCompletedListenPhase();
                }

                _listenRepeatPhase = phase;
                ResetCueSchedulingState();

                ListenRepeatStartEndCueMode cueMode = ModSettings.Current.listenRepeatStartEndCueMode;
                bool useSpeech = cueMode == ListenRepeatStartEndCueMode.Speech || cueMode == ListenRepeatStartEndCueMode.Both;
                if (useSpeech && !(suppressStartupListenAnnouncement && phase == 0 && groupIndex == 0))
                {
                    MenuNarration.Speak(phase == 0 ? "Listen" : "Repeat", interrupt: true);
                }
            }

            TryScheduleListenBoundaryCues(conductor, beatsPerGroup, groupIndex, nowDsp, phase, phaseChanged);
            ArmListenGroupIfNeeded(conductor, beatsPerGroup, groupIndex, phase);
            FlushArmedListenGroup(conductor, nowDsp, phase == 0);

            if (phase == 0)
            {
                RDC.auto = true;
                ApplyListenDucking(conductor, shouldDuck: true);
                return;
            }

            RDC.auto = ShouldForceAutoForUncuedRepeatTarget(controller, conductor, beatsPerGroup);
            ApplyListenDucking(conductor, shouldDuck: false);
        }

        private static bool ShouldForceAutoForUncuedRepeatTarget(scrController controller, scrConductor conductor, int beatsPerGroup)
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

            if (!LastListenScheduledSeqIds.Contains(target.seqID))
            {
                return true;
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

        private static void TryScheduleListenBoundaryCues(scrConductor conductor, int beatsPerGroup, int currentGroupIndex, double nowDsp, int phase, bool phaseChanged)
        {
            scrLevelMaker levelMaker = ADOBase.lm;
            if (conductor == null || levelMaker == null || levelMaker.listFloors == null || beatsPerGroup <= 0)
            {
                return;
            }

            List<scrFloor> floors = levelMaker.listFloors;
            bool suppressStartupListenStartMarker = _listenRepeatStartupForced && phase == 0 && currentGroupIndex == 0;
            foreach (BoundaryCueRequest request in GetBoundaryCueRequests(currentGroupIndex, phase, phaseChanged))
            {
                if ((request.ListenGroupIndex & 1) != 0)
                {
                    continue;
                }

                double listenStartBeat = request.ListenGroupIndex * (double)beatsPerGroup;
                double listenEndBeat = listenStartBeat + beatsPerGroup;
                double listenStartDsp;
                double repeatStartDsp;
                if (!TryGetCueDspForBeat(conductor, floors, listenStartBeat, out listenStartDsp) ||
                    !TryGetCueDspForBeat(conductor, floors, listenEndBeat, out repeatStartDsp))
                {
                    continue;
                }

                if (!(suppressStartupListenStartMarker && request.ListenGroupIndex == 0))
                {
                    TryScheduleListenBoundaryCue(
                        conductor,
                        floors,
                        request.ListenGroupIndex,
                        "start",
                        listenStartBeat,
                        TapCueService.GetListenStartCueDurationSeconds(),
                        TapCueService.PlayListenStartAt,
                        TapCueService.PlayListenStartNow,
                        nowDsp,
                        repeatStartDsp,
                        request.AllowImmediateStart);
                }
                TryScheduleListenBoundaryCue(
                    conductor,
                    floors,
                    request.ListenGroupIndex,
                    "end",
                    listenEndBeat,
                    TapCueService.GetListenEndCueDurationSeconds(),
                    TapCueService.PlayListenEndAt,
                    TapCueService.PlayListenEndNow,
                    nowDsp,
                    repeatStartDsp,
                    request.AllowImmediateEnd);
            }
        }

        private sealed class BoundaryCueRequest
        {
            public int ListenGroupIndex;
            public bool AllowImmediateStart;
            public bool AllowImmediateEnd;
        }

        private static IEnumerable<BoundaryCueRequest> GetBoundaryCueRequests(int currentGroupIndex, int phase, bool phaseChanged)
        {
            if (phase == 0)
            {
                yield return new BoundaryCueRequest
                {
                    ListenGroupIndex = currentGroupIndex,
                    AllowImmediateStart = phaseChanged,
                    AllowImmediateEnd = false
                };

                yield return new BoundaryCueRequest
                {
                    ListenGroupIndex = currentGroupIndex + 2,
                    AllowImmediateStart = false,
                    AllowImmediateEnd = false
                };
                yield break;
            }

            yield return new BoundaryCueRequest
            {
                ListenGroupIndex = currentGroupIndex - 1,
                AllowImmediateStart = false,
                AllowImmediateEnd = phaseChanged
            };

            yield return new BoundaryCueRequest
            {
                ListenGroupIndex = currentGroupIndex + 1,
                AllowImmediateStart = false,
                AllowImmediateEnd = false
            };
        }

        private static void TryScheduleListenBoundaryCue(
            scrConductor conductor,
            List<scrFloor> floors,
            int listenGroupIndex,
            string markerType,
            double markerBeat,
            double cueDurationSeconds,
            Action<double> scheduleAction,
            Action immediateAction,
            double nowDsp,
            double repeatStartDsp,
            bool allowImmediate)
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
            double untilCueStart = cueStartDsp - nowDsp;
            ListenRepeatStartEndCueMode cueMode = ModSettings.Current.listenRepeatStartEndCueMode;
            bool useSound = cueMode == ListenRepeatStartEndCueMode.Sound || cueMode == ListenRepeatStartEndCueMode.Both;

            if (untilCueStart < 0.0 && cueStartDsp < repeatStartDsp && allowImmediate)
            {
                HandledListenBoundaryCueKeys.Add(key);
                if (useSound)
                {
                    immediateAction();
                }
                return;
            }

            if (untilCueStart < 0.0)
            {
                HandledListenBoundaryCueKeys.Add(key);
                return;
            }

            if (untilCueStart > GetScheduleHorizonSeconds(conductor))
            {
                return;
            }

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

        private static void ArmListenGroupIfNeeded(scrConductor conductor, int beatsPerGroup, int groupIndex, int phase)
        {
            int desiredListenGroupIndex = phase == 0 ? groupIndex : groupIndex + 1;
            if ((desiredListenGroupIndex & 1) != 0)
            {
                return;
            }

            if (_armedListenGroup != null && _armedListenGroup.ListenGroupIndex == desiredListenGroupIndex)
            {
                return;
            }

            _armedListenGroup = BuildArmedListenGroup(conductor, beatsPerGroup, desiredListenGroupIndex);
        }

        private static ArmedListenGroup BuildArmedListenGroup(scrConductor conductor, int beatsPerGroup, int listenGroupIndex)
        {
            scrLevelMaker levelMaker = ADOBase.lm;
            List<scrFloor> floors = levelMaker != null ? levelMaker.listFloors : null;
            if (conductor == null || floors == null || floors.Count == 0 || beatsPerGroup <= 0)
            {
                return null;
            }

            int repeatGroupIndex = listenGroupIndex + 1;
            double listenStartBeat = listenGroupIndex * (double)beatsPerGroup;
            double repeatStartBeat = repeatGroupIndex * (double)beatsPerGroup;
            double repeatEndBeat = repeatStartBeat + beatsPerGroup;
            if (!TryGetEntryTimePitchAdjustedForBeat(floors, listenStartBeat, out double listenStartTime) ||
                !TryGetEntryTimePitchAdjustedForBeat(floors, repeatStartBeat, out double repeatStartTime))
            {
                return null;
            }

            double groupTimeShift = repeatStartTime - listenStartTime;
            ArmedListenGroup group = new ArmedListenGroup
            {
                ListenGroupIndex = listenGroupIndex,
                RepeatGroupIndex = repeatGroupIndex,
                ListenStartDsp = conductor.dspTimeSongPosZero + listenStartTime,
                RepeatStartDsp = conductor.dspTimeSongPosZero + repeatStartTime
            };

            for (int i = 0; i < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                if (floor == null || floor.auto)
                {
                    continue;
                }

                if (floor.entryBeat < repeatStartBeat || floor.entryBeat >= repeatEndBeat)
                {
                    continue;
                }

                if (HandledSeqIds.Contains(floor.seqID))
                {
                    continue;
                }

                double previewDueDsp = conductor.dspTimeSongPosZero + floor.entryTimePitchAdj - groupTimeShift;
                if (previewDueDsp < group.ListenStartDsp || previewDueDsp >= group.RepeatStartDsp)
                {
                    continue;
                }

                group.Cues.Add(new ListenCueEvent
                {
                    SeqId = floor.seqID,
                    CueDsp = previewDueDsp
                });
            }

            group.Cues.Sort((a, b) => a.CueDsp.CompareTo(b.CueDsp));
            return group;
        }

        private static void FlushArmedListenGroup(scrConductor conductor, double nowDsp, bool allowImmediateLatePlayback)
        {
            if (_armedListenGroup == null || conductor == null)
            {
                return;
            }

            double scheduleHorizon = GetScheduleHorizonSeconds(conductor);
            while (_armedListenGroup.NextCueIndex < _armedListenGroup.Cues.Count)
            {
                ListenCueEvent cue = _armedListenGroup.Cues[_armedListenGroup.NextCueIndex];
                if (cue.CueDsp > nowDsp + scheduleHorizon)
                {
                    break;
                }

                _armedListenGroup.NextCueIndex++;
                _armedListenGroup.ScheduledSeqIds.Add(cue.SeqId);
                if (cue.CueDsp > nowDsp)
                {
                    TapCueService.PlayCueAt(cue.CueDsp);
                }
                else if (allowImmediateLatePlayback && cue.CueDsp < _armedListenGroup.RepeatStartDsp)
                {
                    TapCueService.PlayCueNow();
                }
            }
        }

        private static void ClearArmedListenGroup()
        {
            _armedListenGroup = null;
        }

        private static void ResetCheckpointRecovery()
        {
            _checkpointRecoveryActive = false;
            _checkpointRecoveryTargetListenGroupIndex = -1;
        }

        private static bool HandleCheckpointRecovery(scrConductor conductor, double currentBeat, int beatsPerGroup)
        {
            if (!_checkpointRecoveryActive || beatsPerGroup <= 0)
            {
                return false;
            }

            double targetStartBeat = _checkpointRecoveryTargetListenGroupIndex * (double)beatsPerGroup;
            if (currentBeat + ListenRepeatBoundaryEpsilon >= targetStartBeat)
            {
                ResetCheckpointRecovery();
                _listenRepeatPhase = -1;
                LastListenScheduledSeqIds.Clear();
                ClearArmedListenGroup();
                ResetAllSchedulingState();
                return false;
            }

            TapCueService.StopAllCues();
            ClearArmedListenGroup();
            RDC.auto = true;
            ApplyListenDucking(conductor, shouldDuck: false);
            return true;
        }

        internal static void PrimeListenRepeatStart(scrController controller)
        {
            if (controller == null || ModSettings.Current.playMode != PlayMode.ListenRepeat || LevelPreview.IsActive)
            {
                return;
            }

            scrConductor conductor = ADOBase.conductor;
            scrFloor current = controller.currFloor;
            if (conductor == null || current == null)
            {
                return;
            }

            int beatsPerGroup = Math.Max(1, ModSettings.Current.patternPreviewBeatsAhead);
            double currentBeat = current.entryBeat;
            int currentGroupIndex = Mathf.FloorToInt((float)(currentBeat / beatsPerGroup));
            double currentGroupStartBeat = currentGroupIndex * (double)beatsPerGroup;
            bool atGroupBoundary = Math.Abs(currentBeat - currentGroupStartBeat) <= ListenRepeatBoundaryEpsilon;
            bool atFullListenGroupStart = atGroupBoundary && (currentGroupIndex & 1) == 0;

            if (!atFullListenGroupStart)
            {
                _listenRepeatStartupForced = false;
                ResetCheckpointRecovery();
                _checkpointRecoveryActive = true;
                _checkpointRecoveryTargetListenGroupIndex = currentGroupIndex + ((currentGroupIndex & 1) == 0 ? 2 : 1);
                _listenRepeatPhase = -1;
                LastListenScheduledSeqIds.Clear();
                ClearArmedListenGroup();
                ResetAllSchedulingState();
                TapCueService.StopAllCues();
                return;
            }

            _listenRepeatStartupForced = currentGroupIndex == 0;
            ResetCheckpointRecovery();
            _listenRepeatPhase = -1;
            LastListenScheduledSeqIds.Clear();
            ClearArmedListenGroup();
            ResetAllSchedulingState();

            _armedListenGroup = BuildArmedListenGroup(conductor, beatsPerGroup, currentGroupIndex);
            FlushArmedListenGroup(conductor, conductor.dspTime, allowImmediateLatePlayback: false);
        }

        private static void ResetListenRepeatRunState()
        {
            _listenRepeatPhase = -1;
            LastListenScheduledSeqIds.Clear();
            ClearArmedListenGroup();
            _listenRepeatStartupForced = false;
            ResetCheckpointRecovery();
            ResetAllSchedulingState();
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
                if (untilPreview < 0.0)
                {
                    HandledSeqIds.Add(floor.seqID);
                    continue;
                }

                if (untilPreview > GetScheduleHorizonSeconds(conductor))
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

        private static void ResetCueSchedulingState()
        {
            HandledSeqIds.Clear();
        }

        private static void ResetAllSchedulingState()
        {
            ResetCueSchedulingState();
            HandledListenBoundaryCueKeys.Clear();
        }

        private static void CaptureCompletedListenPhase()
        {
            LastListenScheduledSeqIds.Clear();
            if (_armedListenGroup == null)
            {
                return;
            }

            foreach (int seqId in _armedListenGroup.ScheduledSeqIds)
            {
                LastListenScheduledSeqIds.Add(seqId);
            }
        }

        private static double GetScheduleHorizonSeconds(scrConductor conductor)
        {
            double beatSeconds = GetBeatDurationSeconds(conductor);
            if (beatSeconds > 0.0)
            {
                return Math.Max(beatSeconds, Time.unscaledDeltaTime);
            }

            return Math.Max(Time.unscaledDeltaTime, Time.smoothDeltaTime);
        }

        private static double GetBeatDurationSeconds(scrConductor conductor)
        {
            if (conductor == null)
            {
                return 0.0;
            }

            float pitch = conductor.song != null && conductor.song.pitch > 0f ? conductor.song.pitch : 1f;
            if (conductor.crotchetAtStart <= 0.0 || pitch <= 0f)
            {
                return 0.0;
            }

            return conductor.crotchetAtStart / pitch;
        }

    }

    [HarmonyLib.HarmonyPatch(typeof(scrController), "PlayerControl_Enter")]
    internal static class PatternPreviewPlayerControlEnterPatch
    {
        private static void Postfix()
        {
            PatternPreview.PrimeListenRepeatStart(ADOBase.controller);
        }
    }
}
