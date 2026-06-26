using System;
using System.Reflection;
using HarmonyLib;
using MobileMenu;
using Rewired;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class ListenRepeatPlayerMenu
    {
        private static readonly FieldInfo PlayerControllersField = AccessTools.Field(typeof(PlayerSelect), "playerControllers");
        private static readonly FieldInfo PlayerJoysticksField = AccessTools.Field(typeof(PlayerSelect), "playerJoysticks");
        private static readonly FieldInfo ShowingAtStartupField = AccessTools.Field(typeof(PlayerSelect), "showingAtStartup");

        private static bool _open;
        private static bool _continuing;
        private static PlayerSelect _playerSelect;
        private static PlayerSelect _sessionPlayerSelect;
        private static bool _sessionMenuHandled;
        private static int _playerCount;
        private static int _selectedIndex;
        private static ListenRepeatPlayerMode[] _workingModes = new ListenRepeatPlayerMode[4];

        public static bool IsOpen => _open;

        public static bool TryOpen(PlayerSelect playerSelect)
        {
            if (_continuing || playerSelect == null || ModSettings.Current.playMode != PlayMode.ListenRepeat || IsShowingAtStartup(playerSelect))
            {
                return false;
            }

            if (_open)
            {
                return true;
            }

            if (ReferenceEquals(_sessionPlayerSelect, playerSelect) && _sessionMenuHandled)
            {
                return true;
            }

            int playerCount = playerSelect.playersSelected.GetValueOrDefault(scrPlayerManager.playerCount);
            if (playerCount < 1)
            {
                playerCount = 1;
            }
            else if (playerCount > 4)
            {
                playerCount = 4;
            }

            _playerSelect = playerSelect;
            _sessionPlayerSelect = playerSelect;
            _sessionMenuHandled = true;
            _playerCount = playerCount;
            _selectedIndex = 0;
            Array.Copy(ModSettings.Current.listenRepeatPlayerModes, _workingModes, _workingModes.Length);
            _open = true;

            ControllerCompat.SetResponsive(false);

            MenuNarration.Speak("Listen-repeat players. Up and down to navigate. Left, right, or enter to change. Choose continue when done.", interrupt: true);
            SpeakSelection();
            return true;
        }

        public static void Tick()
        {
            if (!_open)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow) || MenuGamepad.UpPressed())
            {
                Move(-1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow) || MenuGamepad.DownPressed())
            {
                Move(1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Home))
            {
                _selectedIndex = 0;
                SpeakSelection();
                return;
            }

            if (Input.GetKeyDown(KeyCode.End))
            {
                _selectedIndex = _playerCount;
                SpeakSelection();
                return;
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) || MenuGamepad.LeftPressed() || MenuGamepad.RightPressed())
            {
                ToggleSelection();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || MenuGamepad.ConfirmPressed())
            {
                if (_selectedIndex >= _playerCount)
                {
                    Confirm();
                }
                else
                {
                    ToggleSelection();
                }
            }
        }

        private static void Move(int delta)
        {
            int count = _playerCount + 1;
            _selectedIndex = (_selectedIndex + delta + count) % count;
            SpeakSelection();
        }

        private static void ToggleSelection()
        {
            if (_selectedIndex >= _playerCount)
            {
                SpeakSelection();
                return;
            }

            _workingModes[_selectedIndex] = _workingModes[_selectedIndex] == ListenRepeatPlayerMode.ListenRepeat
                ? ListenRepeatPlayerMode.Vanilla
                : ListenRepeatPlayerMode.ListenRepeat;
            SpeakSelection();
        }

        private static void SpeakSelection()
        {
            int count = _playerCount + 1;
            if (_selectedIndex >= _playerCount)
            {
                MenuNarration.Speak($"Continue, button, {_selectedIndex + 1} of {count}", interrupt: true);
                return;
            }

            MenuNarration.Speak(
                $"Player {_selectedIndex + 1}, {GetModeLabel(_workingModes[_selectedIndex])}, option, {_selectedIndex + 1} of {count}",
                interrupt: true);
        }

        private static void Confirm()
        {
            for (int i = 0; i < _workingModes.Length; i++)
            {
                ModSettings.Current.listenRepeatPlayerModes[i] = _workingModes[i];
            }

            ModSettings.Save();
            MenuNarration.Speak("Listen-repeat player settings saved", interrupt: true);
            ContinuePlayerSelectFinish();
        }

        private static void ContinuePlayerSelectFinish()
        {
            PlayerSelect playerSelect = _playerSelect;
            _open = false;
            _playerSelect = null;

            ControllerCompat.SetResponsive(true);

            if (playerSelect == null)
            {
                return;
            }

            _continuing = true;
            try
            {
                FinishPlayerSelect(playerSelect);
            }
            finally
            {
                _continuing = false;
            }
        }

        private static void FinishPlayerSelect(PlayerSelect playerSelect)
        {
            int playerCount = Math.Max(1, Math.Min(4, playerSelect.playersSelected.GetValueOrDefault(scrPlayerManager.playerCount)));
            ControllerType[] playerControllers = PlayerControllersField?.GetValue(playerSelect) as ControllerType[];
            Joystick[] playerJoysticks = PlayerJoysticksField?.GetValue(playerSelect) as Joystick[];

            scrPlayerManager.ResetPlayersAppearance();
            scrPlayerManager.SetPlayerCount(playerCount);
            RDInput.ReassignControllers(playerCount, playerControllers, playerJoysticks);
            ADOBase.controller.RestartProgress();

            if (playerSelect.pauseMenu.shouldUseGamePauseButtons)
            {
                if (ADOBase.isMobileMenu && playerCount != 1)
                {
                    scnMobileMenu.introPhase = IntroPhase.PlayerSelected;
                    scnMobileMenu.returnToLevelAfterIntroFinished = true;
                    GCS.sceneToLoad = GCNS.sceneLevelSelect;
                    ADOBase.controller.StartLoadingScene();
                }
                else
                {
                    ADOBase.controller.Restart();
                }
            }
            else
            {
                scnMobileMenu.introPhase = IntroPhase.PlayerSelected;
                GCS.sceneToLoad = GCNS.sceneLevelSelect;
                ADOBase.controller.StartLoadingScene();
            }
        }

        public static bool IsListenRepeatEnabledForPlayer(scrPlayer player)
        {
            if (player == null)
            {
                return false;
            }

            int index = player.playerID;
            ListenRepeatPlayerMode[] modes = ModSettings.Current.listenRepeatPlayerModes;
            if (modes == null || index < 0 || index >= modes.Length)
            {
                return true;
            }

            return modes[index] == ListenRepeatPlayerMode.ListenRepeat;
        }

        private static string GetModeLabel(ListenRepeatPlayerMode mode)
        {
            return mode == ListenRepeatPlayerMode.ListenRepeat ? "listen-repeat" : "vanilla";
        }

        private static bool IsShowingAtStartup(PlayerSelect playerSelect)
        {
            return ShowingAtStartupField != null && ShowingAtStartupField.GetValue(playerSelect) is bool showingAtStartup && showingAtStartup;
        }

        public static void BeginPlayerSelectSession(PlayerSelect playerSelect)
        {
            if (playerSelect == null || _open || _continuing)
            {
                return;
            }

            _sessionPlayerSelect = playerSelect;
            _sessionMenuHandled = false;
        }
    }

    [HarmonyPatch(typeof(PlayerSelect), "Finish")]
    internal static class ListenRepeatPlayerMenuFinishPatch
    {
        private static bool Prefix(PlayerSelect __instance)
        {
            return !ListenRepeatPlayerMenu.TryOpen(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerSelect), nameof(PlayerSelect.Show))]
    internal static class ListenRepeatPlayerMenuShowPatch
    {
        private static void Postfix(PlayerSelect __instance)
        {
            ListenRepeatPlayerMenu.BeginPlayerSelectSession(__instance);
        }
    }

    [HarmonyPatch(typeof(scrPlayer), nameof(scrPlayer.Simulated_PlayerControl_Update))]
    internal static class ListenRepeatPerPlayerAutoPatch
    {
        private struct AutoState
        {
            public bool Applied;
            public bool PreviousAuto;
        }

        private static void Prefix(scrPlayer __instance, ref AutoState __state)
        {
            if (!ListenRepeatMode.TryGetAutoForPlayer(__instance, out bool shouldAuto))
            {
                __state = default;
                return;
            }

            __state = new AutoState
            {
                Applied = true,
                PreviousAuto = RDC.auto
            };
            RDC.auto = shouldAuto;
        }

        private static void Postfix(AutoState __state)
        {
            if (__state.Applied)
            {
                RDC.auto = __state.PreviousAuto;
            }
        }
    }
}
