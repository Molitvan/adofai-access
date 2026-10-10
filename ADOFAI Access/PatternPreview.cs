using System;
using System.Collections.Generic;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class PatternPreview
    {
        private static readonly HashSet<int> HandledSeqIds = new HashSet<int>();

        public static void Tick()
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

                if (!PlayModeTiming.TryGetPreviewCueDsp(conductor, floors, floor, out double previewDueDsp))
                {
                    continue;
                }

                FloorCueKind cueKind = PlayModeTiming.GetFloorCueKind(floor);
                double holdEndDsp = 0d;
                if (cueKind == FloorCueKind.Hold)
                {
                    if (!PlayModeTiming.TryGetHoldEndFloor(floor, out scrFloor release) ||
                        !PlayModeTiming.TryGetPreviewCueDsp(conductor, floors, release, out holdEndDsp))
                    {
                        continue;
                    }
                    // Some terminal floors have no usable entryBeat. Keep their
                    // real hold duration if beat-based preview mapping collapses.
                    if (holdEndDsp <= previewDueDsp)
                    {
                        holdEndDsp = previewDueDsp + release.entryTimePitchAdj - floor.entryTimePitchAdj;
                    }
                }

                double untilPreview = previewDueDsp - nowDsp;
                if (untilPreview < 0.0 && (cueKind != FloorCueKind.Hold || holdEndDsp <= nowDsp))
                {
                    HandledSeqIds.Add(floor.seqID);
                    continue;
                }

                if (untilPreview > PlayModeTiming.GetScheduleHorizonSeconds(conductor))
                {
                    continue;
                }

                HandledSeqIds.Add(floor.seqID);
                if (cueKind == FloorCueKind.None)
                {
                    continue;
                }

                bool multiTap = floor.tapsNeeded > 1;
                if (untilPreview >= 0.0)
                {
                    TapCueService.PlayFloorCueAt(cueKind, previewDueDsp, multiTap, holdEndDsp);
                }
                else
                {
                    TapCueService.PlayFloorCueNow(cueKind, multiTap, holdEndDsp);
                }
            }
        }

        internal static void ResetForModeSwitch()
        {
            HandledSeqIds.Clear();
        }

        internal static void Stop()
        {
            HandledSeqIds.Clear();
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(scrController), "PlayerControl_Enter")]
    internal static class PatternPreviewPlayerControlEnterPatch
    {
        private static void Postfix()
        {
            ListenRepeatMode.PrimeStart(ADOBase.controller);
        }
    }
}
