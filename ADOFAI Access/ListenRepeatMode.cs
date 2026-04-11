using System;
using System.Collections.Generic;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class ListenRepeatMode
    {
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

        private sealed class BoundaryCueRequest
        {
            public int ListenGroupIndex;
            public bool AllowImmediateStart;
            public bool AllowImmediateEnd;
        }

        private static int _listenRepeatPhase = -1;
        private static bool _isListenDuckingActive;
        private static float _preDuckSongVolume = 1f;
        private static readonly HashSet<int> HandledSeqIds = new HashSet<int>();
        private static readonly HashSet<int> LastListenScheduledSeqIds = new HashSet<int>();
        private static readonly HashSet<string> HandledListenBoundaryCueKeys = new HashSet<string>();
        private static ArmedListenGroup _armedListenGroup;
        private static bool _listenRepeatStartupForced;
        private static bool _checkpointRecoveryActive;
        private static int _checkpointRecoveryTargetListenGroupIndex = -1;

        internal static void Tick(scrController controller)
        {
            scrConductor conductor = ADOBase.conductor;
            if (conductor == null)
            {
                return;
            }

            if (controller.state != States.PlayerControl)
            {
                TapCueService.StopAllCues();
                ApplyListenDucking(conductor, shouldDuck: false);
                ResetRunState();
                return;
            }

            if (!PlayModeTiming.TryGetCurrentBeat(controller, conductor, out double currentBeat))
            {
                ApplyListenDucking(conductor, shouldDuck: false);
                return;
            }

            double nowDsp = conductor.dspTime;
            int beatsPerGroup = Math.Max(1, ModSettings.Current.listenRepeatGroupBeats);
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

            RDC.auto = ShouldForceAutoForUncuedRepeatTarget(controller);
            ApplyListenDucking(conductor, shouldDuck: false);
        }

        internal static void PrimeStart(scrController controller)
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

            int beatsPerGroup = Math.Max(1, ModSettings.Current.listenRepeatGroupBeats);
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

        internal static void ResetForModeSwitch()
        {
            ResetRunState();
        }

        internal static void Stop()
        {
            TapCueService.StopAllCues();
            ApplyListenDucking(ADOBase.conductor, shouldDuck: false);
            PatternPreview.RestoreAuto();
            ResetRunState();
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
                if (!PlayModeTiming.TryGetCueDspForBeat(conductor, floors, listenStartBeat, out double listenStartDsp) ||
                    !PlayModeTiming.TryGetCueDspForBeat(conductor, floors, listenEndBeat, out double repeatStartDsp))
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

            if (!PlayModeTiming.TryGetCueDspForBeat(conductor, floors, markerBeat, out double boundaryDsp))
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

            if (untilCueStart > PlayModeTiming.GetScheduleHorizonSeconds(conductor))
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
            if (!PlayModeTiming.TryGetEntryTimePitchAdjustedForBeat(floors, listenStartBeat, out double listenStartTime) ||
                !PlayModeTiming.TryGetEntryTimePitchAdjustedForBeat(floors, repeatStartBeat, out double repeatStartTime))
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

            double scheduleHorizon = PlayModeTiming.GetScheduleHorizonSeconds(conductor);
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

        private static void ResetRunState()
        {
            _listenRepeatPhase = -1;
            LastListenScheduledSeqIds.Clear();
            ClearArmedListenGroup();
            _listenRepeatStartupForced = false;
            ResetCheckpointRecovery();
            ResetAllSchedulingState();
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
    }
}
