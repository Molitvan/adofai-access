using DavyKager;
using HarmonyLib;
using MelonLoader;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

[assembly: MelonInfo(typeof(ADOFAI_Access.Core), "ADOFAI Access", "0.3-alpha", "Molitvan", null)]
[assembly: MelonGame("7th Beat Games", "A Dance of Fire and Ice")]

namespace ADOFAI_Access
{
    public class Core : MelonMod
    {
        private static readonly string ModVersion = typeof(Core).Assembly
            .GetCustomAttributes(typeof(MelonInfoAttribute), inherit: false)
            .OfType<MelonInfoAttribute>()
            .FirstOrDefault()?.Version ?? "unknown";
        internal static string VersionString => ModVersion;

        public override void OnInitializeMelon()
        {
            ModSettings.EnsureLoaded();
            Tolk.Load();
            HarmonyInstance.PatchAll(typeof(Core).Assembly);
            LoggerInstance.Msg("ADOFAI Access Loaded");
        }

        public override void OnLateInitializeMelon()
        {
            if (ModSettings.Current.menuNarrationEnabled)
            {
                Tolk.Output($"ADOFAI Access loaded, version {ModVersion}");
            }
        }

        public override void OnUpdate()
        {
            AccessSettingsMenu.Tick();
            MenuNarration.Tick();
            AccessibleLevelSelectMenu.Tick();
            CustomMenuInputGuard.Tick();
            LevelDataDump.Tick();
            LevelPreview.Tick();
            PatternPreview.Tick();
        }
    }

    internal static class CustomMenuInputGuard
    {
        private static bool _active;
        private static int _suppressPauseUntilFrame = -1;
        private static readonly Dictionary<RDInputType, bool> PreviousInputStates = new Dictionary<RDInputType, bool>();
        private static bool _hadEventSystem;
        private static bool _previousSendNavigationEvents;

        public static bool ShouldBlockInput => AccessSettingsMenu.IsOpen || AccessibleLevelSelectMenu.IsOpen;
        public static bool ShouldSuppressPauseToggle => Time.frameCount <= _suppressPauseUntilFrame;

        public static void SuppressPauseForFrames(int frameCount = 2)
        {
            int until = Time.frameCount + Mathf.Max(1, frameCount);
            if (until > _suppressPauseUntilFrame)
            {
                _suppressPauseUntilFrame = until;
            }
        }

        public static void Tick()
        {
            bool shouldBlock = ShouldBlockInput;
            if (shouldBlock == _active)
            {
                return;
            }

            if (shouldBlock)
            {
                Activate();
            }
            else
            {
                Deactivate();
            }
        }

        private static void Activate()
        {
            _active = true;
            PreviousInputStates.Clear();

            if (RDInput.inputs != null)
            {
                for (int i = 0; i < RDInput.inputs.Count; i++)
                {
                    RDInputType input = RDInput.inputs[i];
                    if (input == null)
                    {
                        continue;
                    }

                    PreviousInputStates[input] = input.isActive;
                    input.isActive = false;
                }
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                _hadEventSystem = true;
                _previousSendNavigationEvents = eventSystem.sendNavigationEvents;
                eventSystem.sendNavigationEvents = false;
            }
            else
            {
                _hadEventSystem = false;
            }
        }

        private static void Deactivate()
        {
            _active = false;

            foreach (KeyValuePair<RDInputType, bool> pair in PreviousInputStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.isActive = pair.Value;
                }
            }
            PreviousInputStates.Clear();

            if (_hadEventSystem)
            {
                EventSystem eventSystem = EventSystem.current;
                if (eventSystem != null)
                {
                    eventSystem.sendNavigationEvents = _previousSendNavigationEvents;
                }
            }

            _hadEventSystem = false;
        }
    }

    [HarmonyPatch(typeof(RDInput), nameof(RDInput.GetState))]
    internal static class RDInputGetStateBlockPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!CustomMenuInputGuard.ShouldBlockInput)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(RDInput), nameof(RDInput.GetMain))]
    internal static class RDInputGetMainBlockPatch
    {
        private static bool Prefix(ref int __result)
        {
            if (!CustomMenuInputGuard.ShouldBlockInput)
            {
                return true;
            }

            __result = 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(RDInput), nameof(RDInput.GetStateKeys))]
    internal static class RDInputGetStateKeysBlockPatch
    {
        private static bool Prefix(ref List<AnyKeyCode> __result)
        {
            if (!CustomMenuInputGuard.ShouldBlockInput)
            {
                return true;
            }

            __result = new List<AnyKeyCode>();
            return false;
        }
    }

    [HarmonyPatch(typeof(scrController), nameof(scrController.TogglePauseGame))]
    internal static class TogglePauseGameWhileCustomMenuPatch
    {
        private static bool Prefix()
        {
            if (CustomMenuInputGuard.ShouldBlockInput || CustomMenuInputGuard.ShouldSuppressPauseToggle)
            {
                return false;
            }

            return true;
        }
    }
}
