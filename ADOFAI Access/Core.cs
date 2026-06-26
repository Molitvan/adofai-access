using HarmonyLib;
using MelonLoader;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

[assembly: MelonInfo(typeof(ADOFAI_Access.Core), "ADOFAI Access", "0.6.1-beta", "Molitvan", null)]
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
            PrismSpeech.Load();
            HarmonyInstance.PatchAll(typeof(Core).Assembly);
            LoggerInstance.Msg("ADOFAI Access Loaded");
        }

        public override void OnLateInitializeMelon()
        {
            if (ModSettings.Current.menuNarrationEnabled)
            {
                PrismSpeech.Output($"ADOFAI Access loaded, version {ModVersion}");
            }
        }

        public override void OnUpdate()
        {
            AccessSettingsMenu.Tick();
            AudioGlossaryMenu.Tick();
            ListenRepeatPlayerMenu.Tick();
            MenuNarration.Tick();
            AccessibleLevelSelectMenu.Tick();
            CustomMenuInputGuard.Tick();
            LevelDataDump.Tick();
            LevelPreview.Tick();
            PlayModeController.Tick();
        }
    }

    // Game 3.1.x moved input responsiveness off scrController (the old
    // `scrController.responsive` field) onto each scrPlayer, driven through
    // scrPlayerManager.SetAllPlayerResponsive(...). These helpers mirror that
    // change so the custom menus can still suspend/restore map navigation input.
    internal static class ControllerCompat
    {
        private static scrPlayer[] GetPlayers()
        {
            scrPlayerManager pm = ADOBase.playerManager;
            return pm != null ? pm.players : null;
        }

        public static bool GetResponsive()
        {
            scrPlayer[] players = GetPlayers();
            if (players != null && players.Length > 0 && players[0] != null)
            {
                return players[0].responsive;
            }

            return false;
        }

        public static void SetResponsive(bool value)
        {
            scrPlayer[] players = GetPlayers();
            if (players == null)
            {
                return;
            }

            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null)
                {
                    players[i].responsive = value;
                }
            }
        }

        // Game 3.2.0 removed ADOBase.isPlayingLevel (and ADOBase.isFeaturedLevel).
        // The old property was (isOfficialLevel || isFeaturedLevel) && !isLevelSelect.
        // In 3.2.0 isOfficialLevel already excludes CLS levels and the level editor
        // (controller != null && !isCLSLevel && !isLevelEditor), so the closest
        // equivalent is "an official level that is not the level-select scene".
        public static bool IsPlayingLevel()
        {
            return ADOBase.isOfficialLevel && !ADOBase.isLevelSelect;
        }
    }

    internal static class CustomMenuInputGuard
    {
        private static bool _active;
        private static int _suppressPauseUntilFrame = -1;
        private static int _suppressBeginUntilFrame = -1;
        private static readonly Dictionary<RDInputType, bool> PreviousInputStates = new Dictionary<RDInputType, bool>();
        private static bool _hadEventSystem;
        private static bool _previousSendNavigationEvents;

        public static bool ShouldBlockInput => AccessSettingsMenu.IsOpen || AccessibleLevelSelectMenu.IsOpen || ListenRepeatPlayerMenu.IsOpen || AudioGlossaryMenu.IsOpen;
        public static bool ShouldSuppressPauseToggle => Time.frameCount <= _suppressPauseUntilFrame;

        // While a custom menu is open (or for a short window after it closes), the press-to-begin /
        // skip "any valid input" check is suppressed, so opening or operating a menu with a controller
        // button on the press-to-begin screen does not also start the level.
        public static bool ShouldSuppressBegin => ShouldBlockInput || Time.frameCount <= _suppressBeginUntilFrame;

        public static void SuppressPauseForFrames(int frameCount = 2)
        {
            int until = Time.frameCount + Mathf.Max(1, frameCount);
            if (until > _suppressPauseUntilFrame)
            {
                _suppressPauseUntilFrame = until;
            }
        }

        public static void SuppressBeginForFrames(int frameCount = 4)
        {
            int until = Time.frameCount + Mathf.Max(1, frameCount);
            if (until > _suppressBeginUntilFrame)
            {
                _suppressBeginUntilFrame = until;
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
            SuppressBeginForFrames();
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
            SuppressBeginForFrames();

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

    // The press-to-begin / skip flow starts the level when any valid input is triggered, which reads
    // Input.anyKeyDown / main-press directly (not via the RDInput guard). Suppress it while a custom
    // menu is open (and briefly after) so opening or operating a menu with a controller button on the
    // press-to-begin screen does not also begin the level.
    [HarmonyPatch(typeof(scrPlayerManager), nameof(scrPlayerManager.AnyValidInputWasTriggered))]
    internal static class AnyValidInputWhileCustomMenuPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (CustomMenuInputGuard.ShouldSuppressBegin)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
