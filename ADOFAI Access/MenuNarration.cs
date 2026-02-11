using System;
using System.Globalization;
using System.Text.RegularExpressions;
using DavyKager;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ADOFAI_Access
{
    internal static class MenuNarration
    {
        private static readonly Regex RichTextRegex = new Regex("<.*?>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex("\\s+", RegexOptions.Compiled);

        private static int _lastSelectedId = -1;
        private static string _lastSpoken = string.Empty;
        private static float _lastSpokenAt;
        private static string _lastAnnouncedWorld = string.Empty;
        private static float _lastLevelStartAt;
        private static float _lastLevelEndAt;
        private static float _lastDeathAt;

        public static void Tick()
        {
            if (ADOBase.isLevelEditor)
            {
                return;
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                _lastSelectedId = -1;
                return;
            }

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null || !selected.activeInHierarchy)
            {
                return;
            }

            int selectedId = selected.GetInstanceID();
            if (selectedId == _lastSelectedId)
            {
                return;
            }

            _lastSelectedId = selectedId;

            if (!TryDescribeSelected(selected, out string label, out string controlType, out string valueState))
            {
                return;
            }

            Speak(ComposePhrase(label, controlType, valueState), interrupt: true);
        }

        public static void SpeakFocusedButton(GeneralPauseButton button)
        {
            if (button == null || ADOBase.isLevelEditor)
            {
                return;
            }

            string label;
            string controlType;
            string valueState;

            switch (button)
            {
                case PauseButton pauseButton:
                    label = BestOf(pauseButton.label != null ? pauseButton.label.text : null, pauseButton.name);
                    controlType = "button";
                    valueState = string.Empty;
                    break;
                case PauseSettingButton settingButton:
                    label = BestOf(settingButton.label != null ? settingButton.label.text : null, settingButton.name);
                    controlType = DetermineSettingControlType(settingButton);
                    valueState = BestOf(settingButton.valueLabel != null ? settingButton.valueLabel.text : null, string.Empty);
                    break;
                case PauseLevelButton levelButton:
                    label = ExtractPauseLevelLabel(levelButton);
                    controlType = "button";
                    valueState = string.Empty;
                    break;
                case SettingsTabButton tabButton:
                    label = BestOf(tabButton.label != null ? tabButton.label.text : null, tabButton.name);
                    controlType = "tab";
                    valueState = string.Empty;
                    break;
                case SocialPauseButton socialButton:
                    label = BestOf(socialButton.label != null ? socialButton.label.text : null, socialButton.name);
                    controlType = "button";
                    valueState = string.Empty;
                    break;
                default:
                    label = BestOf(button.name);
                    controlType = "item";
                    valueState = string.Empty;
                    break;
            }

            Speak(ComposePhrase(label, controlType, valueState), interrupt: true);
        }

        public static void SpeakSettingValueChange(PauseSettingButton settingButton, SettingsMenu.Interaction action)
        {
            if (settingButton == null || ADOBase.isLevelEditor)
            {
                return;
            }

            if (action == SettingsMenu.Interaction.Refresh || action == SettingsMenu.Interaction.ActivateInfo)
            {
                return;
            }

            string label = BestOf(settingButton.label != null ? settingButton.label.text : null, settingButton.name);
            string controlType = DetermineSettingControlType(settingButton);
            string valueState = BestOf(settingButton.valueLabel != null ? settingButton.valueLabel.text : null, string.Empty);
            string normalizedValue = NormalizeValueState(label, controlType, valueState);
            if (string.IsNullOrEmpty(normalizedValue))
            {
                return;
            }

            Speak(normalizedValue, interrupt: true);
        }

        public static void Speak(string text, bool interrupt)
        {
            string normalized = NormalizeText(text);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            float now = Time.unscaledTime;
            bool repeated = normalized.Equals(_lastSpoken, StringComparison.OrdinalIgnoreCase) && now - _lastSpokenAt < 0.35f;
            if (repeated)
            {
                return;
            }

            _lastSpoken = normalized;
            _lastSpokenAt = now;
            Tolk.Output(normalized, interrupt);
        }

        public static void SpeakWorldSelection(string world)
        {
            string normalizedWorld = NormalizeText(world);
            if (string.IsNullOrEmpty(normalizedWorld))
            {
                _lastAnnouncedWorld = string.Empty;
                return;
            }

            if (normalizedWorld == "0")
            {
                _lastAnnouncedWorld = normalizedWorld;
                return;
            }

            if (string.Equals(_lastAnnouncedWorld, normalizedWorld, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastAnnouncedWorld = normalizedWorld;
            Speak($"World {normalizedWorld}", interrupt: true);
        }

        public static void SpeakLevelGetReady()
        {
            if (ADOBase.isLevelEditor)
            {
                return;
            }

            Speak("Get ready", interrupt: true);
        }

        public static void SpeakLevelStart()
        {
            if (ADOBase.isLevelEditor)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastLevelStartAt < 0.5f)
            {
                return;
            }

            _lastLevelStartAt = now;
            Speak("Go", interrupt: true);
        }

        public static void SpeakLevelEnd()
        {
            if (ADOBase.isLevelEditor)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastLevelEndAt < 0.5f)
            {
                return;
            }

            _lastLevelEndAt = now;
            Speak("Level complete", interrupt: true);
        }

        public static void SpeakDeath()
        {
            if (ADOBase.isLevelEditor)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastDeathAt < 0.5f)
            {
                return;
            }

            _lastDeathAt = now;
            Speak("You died", interrupt: true);
        }

        private static bool TryDescribeSelected(GameObject selected, out string label, out string controlType, out string valueState)
        {
            label = string.Empty;
            controlType = string.Empty;
            valueState = string.Empty;

            Toggle toggle = selected.GetComponent<Toggle>();
            if (toggle != null)
            {
                label = BestOf(FindTextOnObject(selected), selected.name);
                controlType = "toggle";
                valueState = toggle.isOn ? "on" : "off";
                return true;
            }

            Slider slider = selected.GetComponent<Slider>();
            if (slider != null)
            {
                label = BestOf(FindTextOnObject(selected), selected.name);
                controlType = "slider";
                valueState = slider.wholeNumbers
                    ? ((int)slider.value).ToString(CultureInfo.InvariantCulture)
                    : slider.value.ToString("0.##", CultureInfo.InvariantCulture);
                return true;
            }

            TMP_Dropdown tmpDropdown = selected.GetComponent<TMP_Dropdown>();
            if (tmpDropdown != null)
            {
                label = BestOf(FindTextOnObject(selected), selected.name);
                controlType = "dropdown";
                valueState = BestOf(tmpDropdown.captionText != null ? tmpDropdown.captionText.text : null, "selected");
                return true;
            }

            Dropdown dropdown = selected.GetComponent<Dropdown>();
            if (dropdown != null)
            {
                label = BestOf(FindTextOnObject(selected), selected.name);
                controlType = "dropdown";
                valueState = BestOf(dropdown.captionText != null ? dropdown.captionText.text : null, "selected");
                return true;
            }

            TMP_InputField tmpInput = selected.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                label = BestOf(tmpInput.placeholder != null ? tmpInput.placeholder.GetComponent<TMP_Text>()?.text : null, FindTextOnObject(selected), selected.name);
                controlType = "text field";
                valueState = BestOf(tmpInput.text, "empty");
                return true;
            }

            InputField input = selected.GetComponent<InputField>();
            if (input != null)
            {
                label = BestOf(input.placeholder != null ? input.placeholder.GetComponent<Text>()?.text : null, FindTextOnObject(selected), selected.name);
                controlType = "text field";
                valueState = BestOf(input.text, "empty");
                return true;
            }

            Button button = selected.GetComponent<Button>();
            if (button != null)
            {
                label = BestOf(FindTextOnObject(selected), selected.name);
                controlType = "button";
                valueState = string.Empty;
                return true;
            }

            string fallbackLabel = BestOf(FindTextOnObject(selected), selected.name);
            if (string.IsNullOrEmpty(fallbackLabel))
            {
                return false;
            }

            label = fallbackLabel;
            controlType = "item";
            valueState = string.Empty;
            return true;
        }

        private static string DetermineSettingControlType(PauseSettingButton settingButton)
        {
            if (settingButton == null)
            {
                return "setting";
            }

            string settingType = settingButton.type ?? string.Empty;
            if (settingType.Equals("Bool", StringComparison.OrdinalIgnoreCase))
            {
                return "toggle";
            }

            if (settingType.Equals("Action", StringComparison.OrdinalIgnoreCase))
            {
                return "button";
            }

            if (settingType.Equals("Int", StringComparison.OrdinalIgnoreCase) && settingButton.hasRange)
            {
                return "slider";
            }

            if (settingType.Equals("Resolution", StringComparison.OrdinalIgnoreCase) || settingType.Equals("Samples", StringComparison.OrdinalIgnoreCase) || settingType.StartsWith("Enum:", StringComparison.OrdinalIgnoreCase) || settingType.Equals("Language", StringComparison.OrdinalIgnoreCase))
            {
                return "option";
            }

            return "setting";
        }

        private static string ExtractPauseLevelLabel(PauseLevelButton levelButton)
        {
            if (levelButton == null)
            {
                return string.Empty;
            }

            string restart = null;
            if (levelButton.restartLabel != null && levelButton.restartLabel.gameObject.activeInHierarchy)
            {
                restart = levelButton.restartLabel.text;
            }

            return BestOf(restart, levelButton.label != null ? levelButton.label.text : null, levelButton.levelName, levelButton.name);
        }

        private static string FindTextOnObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            TMP_Text ownTmpText = gameObject.GetComponent<TMP_Text>();
            if (ownTmpText != null)
            {
                return ownTmpText.text;
            }

            Text ownText = gameObject.GetComponent<Text>();
            if (ownText != null)
            {
                return ownText.text;
            }

            TMP_Text childTmp = gameObject.GetComponentInChildren<TMP_Text>(includeInactive: false);
            if (childTmp != null)
            {
                return childTmp.text;
            }

            Text childText = gameObject.GetComponentInChildren<Text>(includeInactive: false);
            return childText != null ? childText.text : string.Empty;
        }

        private static string ComposePhrase(string label, string controlType, string valueState)
        {
            string a = BestOf(label);
            string b = NormalizeValueState(a, controlType, valueState);
            string c = BestOf(controlType, "item");

            if (!string.IsNullOrEmpty(b))
            {
                return NormalizeText($"{a}, {b}, {c}");
            }

            return NormalizeText($"{a}, {c}");
        }

        private static string NormalizeValueState(string label, string controlType, string valueState)
        {
            string value = NormalizeText(valueState);
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (controlType == "toggle")
            {
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    return "on";
                }

                if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    return "off";
                }
            }

            return value;
        }

        private static string BestOf(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                string value = NormalizeText(values[i]);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string stripped = RichTextRegex.Replace(text, string.Empty).Trim();
            return WhitespaceRegex.Replace(stripped, " ");
        }
    }

    [HarmonyPatch(typeof(PauseMenu), nameof(PauseMenu.Show))]
    internal static class PauseMenuShowPatch
    {
        private static void Postfix()
        {
            MenuNarration.Speak("Pause menu", interrupt: true);
        }
    }

    [HarmonyPatch(typeof(SettingsMenu), nameof(SettingsMenu.Show))]
    internal static class SettingsMenuShowPatch
    {
        private static void Postfix()
        {
            MenuNarration.Speak("Settings", interrupt: true);
        }
    }

    [HarmonyPatch(typeof(PauseButton), nameof(PauseButton.SetFocus))]
    internal static class PauseButtonFocusPatch
    {
        private static void Postfix(PauseButton __instance, bool focus)
        {
            if (focus)
            {
                MenuNarration.SpeakFocusedButton(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(PauseSettingButton), nameof(PauseSettingButton.SetFocus))]
    internal static class PauseSettingButtonFocusPatch
    {
        private static void Postfix(PauseSettingButton __instance, bool focus)
        {
            if (focus)
            {
                MenuNarration.SpeakFocusedButton(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(PauseLevelButton), nameof(PauseLevelButton.SetFocus))]
    internal static class PauseLevelButtonFocusPatch
    {
        private static void Postfix(PauseLevelButton __instance, bool focus)
        {
            if (focus)
            {
                MenuNarration.SpeakFocusedButton(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(SocialPauseButton), nameof(SocialPauseButton.SetFocus))]
    internal static class SocialPauseButtonFocusPatch
    {
        private static void Postfix(SocialPauseButton __instance, bool focus)
        {
            if (focus)
            {
                MenuNarration.SpeakFocusedButton(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(SettingsTabButton), nameof(SettingsTabButton.SetFocus), new Type[] { typeof(bool) })]
    internal static class SettingsTabButtonFocusPatch
    {
        private static void Postfix(SettingsTabButton __instance, bool focus)
        {
            if (focus)
            {
                MenuNarration.SpeakFocusedButton(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(SettingsMenu), nameof(SettingsMenu.UpdateSetting))]
    internal static class SettingsMenuValueChangePatch
    {
        private static void Postfix(PauseSettingButton setting, SettingsMenu.Interaction action)
        {
            MenuNarration.SpeakSettingValueChange(setting, action);
        }
    }

    [HarmonyPatch(typeof(LevelSelectIsland), nameof(LevelSelectIsland.SelectWorld))]
    internal static class LevelSelectWorldSelectionPatch
    {
        private static void Postfix(string world)
        {
            MenuNarration.SpeakWorldSelection(world);
        }
    }

    [HarmonyPatch(typeof(scrPortal), nameof(scrPortal.ExpandPortal))]
    internal static class PortalExpandPatch
    {
        private static void Postfix(scrPortal __instance, bool expand)
        {
            if (!expand || __instance == null)
            {
                return;
            }

            MenuNarration.SpeakWorldSelection(__instance.world);
        }
    }

    [HarmonyPatch(typeof(scnLevelSelect), "Update")]
    internal static class LevelSelectUpdateWorldTrackingPatch
    {
        private static void Postfix(scnLevelSelect __instance)
        {
            if (__instance == null)
            {
                return;
            }

            MenuNarration.SpeakWorldSelection(__instance.lastVisitedWorld);
        }
    }

    [HarmonyPatch(typeof(scrCountdown), nameof(scrCountdown.ShowGetReady))]
    internal static class LevelGetReadyPatch
    {
        private static void Postfix()
        {
            MenuNarration.SpeakLevelGetReady();
        }
    }

    [HarmonyPatch(typeof(scrController), "PlayerControl_Enter")]
    internal static class LevelStartPatch
    {
        private static void Postfix()
        {
            if (ADOBase.isPlayingLevel)
            {
                MenuNarration.SpeakLevelStart();
            }
        }
    }

    [HarmonyPatch(typeof(scrController), "Won_Enter")]
    internal static class LevelWonPatch
    {
        private static void Postfix()
        {
            if (ADOBase.isPlayingLevel)
            {
                MenuNarration.SpeakLevelEnd();
            }
        }
    }

    [HarmonyPatch(typeof(scrController), "Fail2Action")]
    internal static class LevelDeathPatch
    {
        private static void Postfix()
        {
            if (ADOBase.isPlayingLevel)
            {
                MenuNarration.SpeakDeath();
            }
        }
    }
}
