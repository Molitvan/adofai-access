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
        private const KeyCode NarrationToggleKey = KeyCode.F4;
        private static readonly Regex RichTextRegex = new Regex("<.*?>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex("\\s+", RegexOptions.Compiled);

        private static int _lastSelectedId = -1;
        private static string _lastSpoken = string.Empty;
        private static float _lastSpokenAt;
        private static string _lastAnnouncedWorld = string.Empty;
        private static float _lastLevelStartAt;
        private static float _lastLevelEndAt;
        private static float _lastDeathAt;
        private static bool _clsLeftPanelOpen;
        private static bool _clsRightPanelOpen;
        private static string _lastClsDisplayedTitle = string.Empty;
        private static string _lastClsFocusedOptionKey = string.Empty;
        private static bool _wasInClsScene;
        private static ClsLeftSection _lastClsLeftSection = ClsLeftSection.None;
        private static bool _lastClsSearchFocused;
        private static string _lastClsSearchText = string.Empty;

        private enum ClsLeftSection
        {
            None = 0,
            Search = 1,
            SortBy = 2,
            Other = 3
        }

        public static void Tick()
        {
            HandleNarrationToggleHotkey();

            if (AccessSettingsMenu.IsOpen)
            {
                _lastSelectedId = -1;
                return;
            }

            if (!ModSettings.Current.menuNarrationEnabled)
            {
                _lastSelectedId = -1;
                return;
            }

            if (ADOBase.isLevelEditor)
            {
                return;
            }

            HandleCustomLevelsFallbackNarration();

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

        private static void HandleCustomLevelsFallbackNarration()
        {
            bool inCls = ADOBase.sceneName == GCNS.sceneCustomLevelSelect && ADOBase.cls != null;
            if (!inCls)
            {
                _wasInClsScene = false;
                _clsLeftPanelOpen = false;
                _clsRightPanelOpen = false;
                _lastClsDisplayedTitle = string.Empty;
                _lastClsFocusedOptionKey = string.Empty;
                _lastClsLeftSection = ClsLeftSection.None;
                _lastClsSearchFocused = false;
                _lastClsSearchText = string.Empty;
                return;
            }

            scnCLS cls = ADOBase.cls;
            if (!_wasInClsScene)
            {
                _wasInClsScene = true;
                Speak("Custom levels", interrupt: true);
            }

            if (cls.optionsPanels != null)
            {
                bool leftOpen = cls.optionsPanels.showingLeftPanel;
                bool rightOpen = cls.optionsPanels.showingRightPanel;
                if (leftOpen != _clsLeftPanelOpen)
                {
                    _clsLeftPanelOpen = leftOpen;
                    SpeakCustomLevelsPanel(left: true, show: leftOpen);
                }

                if (rightOpen != _clsRightPanelOpen)
                {
                    _clsRightPanelOpen = rightOpen;
                    SpeakCustomLevelsPanel(left: false, show: rightOpen);
                }

                HandleClsSearchNarration(cls.optionsPanels);
                HandleClsPanelFocusNarration(cls.optionsPanels);
            }

            string title = cls.portalName != null ? NormalizeText(cls.portalName.text) : string.Empty;
            bool suppressLevelAnnouncement = cls.optionsPanels != null && (cls.optionsPanels.searchMode || cls.optionsPanels.showingLeftPanel);
            if (suppressLevelAnnouncement)
            {
                if (!string.IsNullOrEmpty(title))
                {
                    _lastClsDisplayedTitle = title;
                }
                return;
            }

            if (string.IsNullOrEmpty(title) || string.Equals(title, _lastClsDisplayedTitle, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastClsDisplayedTitle = title;
            string artist = cls.portalArtist != null ? NormalizeText(cls.portalArtist.text) : string.Empty;
            bool unavailable = cls.levelDeleted;
            string valueState = unavailable ? "unavailable" : (string.IsNullOrEmpty(artist) ? string.Empty : "by " + artist);
            Speak(ComposePhrase(title, "level", valueState), interrupt: true);
        }

        private static void HandleClsPanelFocusNarration(OptionsPanelsCLS panel)
        {
            if (panel == null)
            {
                _lastClsFocusedOptionKey = string.Empty;
                _lastClsLeftSection = ClsLeftSection.None;
                return;
            }

            bool leftOpen = panel.showingLeftPanel;
            bool rightOpen = panel.showingRightPanel;
            if (!leftOpen && !rightOpen)
            {
                _lastClsFocusedOptionKey = string.Empty;
                _lastClsLeftSection = ClsLeftSection.None;
                return;
            }

            if (panel.searchMode && panel.searchInputField != null && panel.searchInputField.isFocused)
            {
                return;
            }

            OptionsPanelsCLS.Option[] options = leftOpen ? panel.leftPanelOptions : panel.rightPanelOptions;
            if (options == null)
            {
                return;
            }

            OptionsPanelsCLS.Option focused = null;
            for (int i = 0; i < options.Length; i++)
            {
                OptionsPanelsCLS.Option option = options[i];
                if (option != null && option.highlighted)
                {
                    focused = option;
                    break;
                }
            }

            if (focused == null)
            {
                return;
            }

            if (leftOpen)
            {
                ClsLeftSection section = GetClsLeftSection(focused.name);
                if (section != _lastClsLeftSection)
                {
                    _lastClsLeftSection = section;
                    if (section == ClsLeftSection.Search)
                    {
                        Speak("Search section", interrupt: true);
                    }
                    else if (section == ClsLeftSection.SortBy)
                    {
                        Speak("Sort by section", interrupt: true);
                    }
                }
            }
            else
            {
                _lastClsLeftSection = ClsLeftSection.None;
            }

            string side = leftOpen ? "L" : "R";
            string key = side + ":" + focused.name;
            if (string.Equals(key, _lastClsFocusedOptionKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastClsFocusedOptionKey = key;
            SpeakClsOption(focused);
        }

        private static void HandleClsSearchNarration(OptionsPanelsCLS panel)
        {
            if (panel == null || panel.searchInputField == null)
            {
                _lastClsSearchFocused = false;
                _lastClsSearchText = string.Empty;
                return;
            }

            if (!panel.searchMode)
            {
                _lastClsSearchFocused = false;
                _lastClsSearchText = string.Empty;
                return;
            }

            InputField field = panel.searchInputField;
            bool focused = field.isFocused;
            string text = NormalizeText(field.text);

            if (focused && !_lastClsSearchFocused)
            {
                string valueState = string.IsNullOrEmpty(text) ? "empty" : text;
                Speak(ComposePhrase("Find", "text field", valueState), interrupt: true);
            }
            else if (!focused && _lastClsSearchFocused)
            {
                if (string.IsNullOrEmpty(text))
                {
                    Speak("Search cleared", interrupt: true);
                }
                else if (!string.Equals(text, _lastClsSearchText, StringComparison.OrdinalIgnoreCase))
                {
                    Speak("Search " + text, interrupt: true);
                }
            }

            _lastClsSearchFocused = focused;
            _lastClsSearchText = text;
        }

        private static ClsLeftSection GetClsLeftSection(OptionsPanelsCLS.OptionName option)
        {
            if (option == OptionsPanelsCLS.OptionName.Find)
            {
                return ClsLeftSection.Search;
            }

            if (option == OptionsPanelsCLS.OptionName.Difficulty ||
                option == OptionsPanelsCLS.OptionName.LastPlayed ||
                option == OptionsPanelsCLS.OptionName.Song ||
                option == OptionsPanelsCLS.OptionName.Artist ||
                option == OptionsPanelsCLS.OptionName.Author)
            {
                return ClsLeftSection.SortBy;
            }

            return ClsLeftSection.Other;
        }

        public static void SpeakFocusedButton(GeneralPauseButton button)
        {
            if (AccessSettingsMenu.IsOpen || !ModSettings.Current.menuNarrationEnabled || button == null || ADOBase.isLevelEditor)
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
            if (AccessSettingsMenu.IsOpen || !ModSettings.Current.menuNarrationEnabled || settingButton == null || ADOBase.isLevelEditor)
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
            if (!ModSettings.Current.menuNarrationEnabled)
            {
                return;
            }

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

        public static void SpeakForced(string text, bool interrupt)
        {
            string normalized = NormalizeText(text);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            _lastSpoken = normalized;
            _lastSpokenAt = Time.unscaledTime;
            Tolk.Output(normalized, interrupt);
        }

        public static void SpeakWorldSelection(string world)
        {
            if (AccessSettingsMenu.IsOpen || !ModSettings.Current.menuNarrationEnabled)
            {
                return;
            }

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

        public static void SpeakLevelGetReady(string text)
        {
            if (ADOBase.isLevelEditor)
            {
                return;
            }

            Speak(BestOf(text, RDString.Get("status.getReady")), interrupt: true);
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
            Speak(RDString.Get("status.go"), interrupt: true);
        }

        public static void SpeakLevelEnd(string text)
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
            Speak(BestOf(text, RDString.Get("status.congratulations"), "Level complete"), interrupt: true);
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

        public static void SpeakCalibrationMessage(scrCalibrationPlanet calibration)
        {
            if (ADOBase.isLevelEditor || calibration == null)
            {
                return;
            }

            string text = calibration.txtMessage != null ? calibration.txtMessage.text : null;
            Speak(BestOf(text, "Calibration"), interrupt: true);
        }

        public static void SpeakCalibrationResults(scrCalibrationPlanet calibration)
        {
            if (ADOBase.isLevelEditor || calibration == null)
            {
                return;
            }

            string results = calibration.txtResults != null ? calibration.txtResults.text : null;
            if (!string.IsNullOrWhiteSpace(results))
            {
                Speak(results, interrupt: true);
                return;
            }

            SpeakCalibrationMessage(calibration);
        }

        public static void SpeakCustomLevelSelection(scnCLS scene, CustomLevelTile tile)
        {
            if (AccessSettingsMenu.IsOpen || !ModSettings.Current.menuNarrationEnabled || ADOBase.isLevelEditor || scene == null || tile == null)
            {
                return;
            }

            string title = BestOf(tile.title != null ? tile.title.text : null, tile.levelKey, "Custom level");
            string artist = BestOf(tile.artist != null ? tile.artist.text : null, string.Empty);
            bool unavailable = tile.removedText != null && tile.removedText.gameObject.activeInHierarchy;
            string controlType = "level";
            string valueState = unavailable ? "unavailable" : (string.IsNullOrEmpty(artist) ? string.Empty : "by " + artist);
            Speak(ComposePhrase(title, controlType, valueState), interrupt: true);
        }

        public static void SpeakClsOptionByIndex(OptionsPanelsCLS panel, int optionIndex)
        {
            if (AccessSettingsMenu.IsOpen || !ModSettings.Current.menuNarrationEnabled || ADOBase.isLevelEditor || panel == null)
            {
                return;
            }

            OptionsPanelsCLS.Option[] options = panel.showingLeftPanel ? panel.leftPanelOptions : panel.rightPanelOptions;
            if (options == null || optionIndex < 0 || optionIndex >= options.Length)
            {
                return;
            }

            SpeakClsOption(options[optionIndex]);
        }

        public static void SpeakClsOptionByName(OptionsPanelsCLS panel, OptionsPanelsCLS.OptionName name, bool leftOptions)
        {
            if (AccessSettingsMenu.IsOpen || !ModSettings.Current.menuNarrationEnabled || ADOBase.isLevelEditor || panel == null)
            {
                return;
            }

            OptionsPanelsCLS.Option[] options = leftOptions ? panel.leftPanelOptions : panel.rightPanelOptions;
            if (options == null)
            {
                return;
            }

            for (int i = 0; i < options.Length; i++)
            {
                OptionsPanelsCLS.Option option = options[i];
                if (option != null && option.name == name)
                {
                    SpeakClsOption(option);
                    break;
                }
            }
        }

        public static void SpeakCustomLevelsPanel(bool left, bool show)
        {
            if (AccessSettingsMenu.IsOpen || !ModSettings.Current.menuNarrationEnabled || ADOBase.isLevelEditor)
            {
                return;
            }

            string side = left ? "Left panel" : "Right panel";
            string state = show ? "open" : "closed";
            Speak($"{side}, {state}", interrupt: true);
        }

        private static void SpeakClsOption(OptionsPanelsCLS.Option option)
        {
            if (option == null)
            {
                return;
            }

            string label = BestOf(option.text != null ? option.text.text : null, HumanizeIdentifier(option.name.ToString()));
            string valueState = string.Empty;
            string controlType = "option";
            switch (option.name)
            {
                case OptionsPanelsCLS.OptionName.Find:
                    label = "Find";
                    controlType = "text field";
                    string searchText = ADOBase.cls != null && ADOBase.cls.optionsPanels != null && ADOBase.cls.optionsPanels.searchInputField != null
                        ? NormalizeText(ADOBase.cls.optionsPanels.searchInputField.text)
                        : string.Empty;
                    valueState = string.IsNullOrEmpty(searchText) ? "empty" : searchText;
                    break;
                case OptionsPanelsCLS.OptionName.Difficulty:
                case OptionsPanelsCLS.OptionName.LastPlayed:
                case OptionsPanelsCLS.OptionName.Song:
                case OptionsPanelsCLS.OptionName.Artist:
                case OptionsPanelsCLS.OptionName.Author:
                    controlType = "radio button";
                    valueState = option.selected ? "selected" : string.Empty;
                    break;
                case OptionsPanelsCLS.OptionName.SpeedTrial:
                case OptionsPanelsCLS.OptionName.NoFail:
                case OptionsPanelsCLS.OptionName.UnlockKeyLimiter:
                    controlType = "toggle";
                    valueState = option.selected ? "on" : "off";
                    break;
                case OptionsPanelsCLS.OptionName.Delete:
                    controlType = "button";
                    valueState = string.Empty;
                    break;
                default:
                    controlType = "option";
                    valueState = option.selected ? "on" : string.Empty;
                    break;
            }

            Speak(ComposePhrase(label, controlType, valueState), interrupt: true);
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

            string levelToken = GetLevelToken(levelButton.levelName);
            return BestOf(restart, levelToken, levelButton.label != null ? levelButton.label.text : null, levelButton.levelName, levelButton.name);
        }

        private static string GetLevelToken(string levelName)
        {
            string normalized = NormalizeText(levelName);
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            int dash = normalized.LastIndexOf('-');
            if (dash < 0 || dash >= normalized.Length - 1)
            {
                return string.Empty;
            }

            return normalized.Substring(dash + 1);
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

        private static string HumanizeIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return string.Empty;
            }

            return Regex.Replace(identifier.Trim(), "([a-z0-9])([A-Z])", "$1 $2");
        }

        private static void HandleNarrationToggleHotkey()
        {
            if (!Input.GetKeyDown(NarrationToggleKey))
            {
                return;
            }

            ModSettings.Current.menuNarrationEnabled = !ModSettings.Current.menuNarrationEnabled;
            ModSettings.Save();

            if (ModSettings.Current.menuNarrationEnabled)
            {
                SpeakForced("Menu narration on", interrupt: true);
            }
            else
            {
                _lastSelectedId = -1;
                SpeakForced("Menu narration off. You can always turn menu narration back on with F4.", interrupt: true);
            }
        }
    }

    [HarmonyPatch(typeof(PauseMenu), nameof(PauseMenu.Show))]
    internal static class PauseMenuShowPatch
    {
        private static void Postfix()
        {
            if (!ModSettings.Current.menuNarrationEnabled)
            {
                return;
            }
            MenuNarration.Speak("Pause menu", interrupt: true);
        }
    }

    [HarmonyPatch(typeof(SettingsMenu), nameof(SettingsMenu.Show))]
    internal static class SettingsMenuShowPatch
    {
        private static void Postfix()
        {
            if (!ModSettings.Current.menuNarrationEnabled)
            {
                return;
            }
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
        private static void Postfix(scrCountdown __instance)
        {
            Text text = __instance != null ? __instance.GetComponent<Text>() : null;
            MenuNarration.SpeakLevelGetReady(text != null ? text.text : null);
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
                scrController controller = ADOBase.controller;
                string text = controller != null && controller.txtCongrats != null ? controller.txtCongrats.text : null;
                MenuNarration.SpeakLevelEnd(text);
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

    [HarmonyPatch(typeof(scrPressToStart), nameof(scrPressToStart.ShowText))]
    internal static class PressToBeginPatch
    {
        private static void Postfix(scrPressToStart __instance)
        {
            if (__instance == null || ADOBase.isLevelEditor)
            {
                return;
            }

            Text text = __instance.GetComponent<Text>();
            MenuNarration.Speak(text != null ? text.text : RDString.Get(ADOBase.isMobile ? "status.tapToBegin" : "status.pressToBegin"), interrupt: true);
        }
    }

    [HarmonyPatch(typeof(scrCalibrationPlanet), "Start")]
    internal static class CalibrationStartPatch
    {
        private static void Postfix(scrCalibrationPlanet __instance)
        {
            if (!ModSettings.Current.menuNarrationEnabled)
            {
                return;
            }

            MenuNarration.Speak("Calibration", interrupt: true);
            MenuNarration.SpeakCalibrationMessage(__instance);
        }
    }

    [HarmonyPatch(typeof(scrCalibrationPlanet), "SetMessageNumber")]
    internal static class CalibrationMessagePatch
    {
        private static void Postfix(scrCalibrationPlanet __instance)
        {
            MenuNarration.SpeakCalibrationMessage(__instance);
        }
    }

    [HarmonyPatch(typeof(scrCalibrationPlanet), "PostSong")]
    internal static class CalibrationPostSongPatch
    {
        private static void Postfix(scrCalibrationPlanet __instance)
        {
            MenuNarration.SpeakCalibrationResults(__instance);
        }
    }

    [HarmonyPatch(typeof(scnCLS), nameof(scnCLS.SelectLevel))]
    internal static class CustomLevelsSelectionPatch
    {
        private static void Postfix(scnCLS __instance, CustomLevelTile tileToSelect, bool snap)
        {
            MenuNarration.SpeakCustomLevelSelection(__instance, tileToSelect);
        }
    }

    [HarmonyPatch(typeof(OptionsPanelsCLS), nameof(OptionsPanelsCLS.SelectOption), new Type[] { typeof(int) })]
    internal static class CustomLevelsOptionSelectionByIndexPatch
    {
        private static void Postfix(OptionsPanelsCLS __instance, int option)
        {
            if (ADOBase.sceneName == GCNS.sceneCustomLevelSelect && ADOBase.cls != null)
            {
                return;
            }

            MenuNarration.SpeakClsOptionByIndex(__instance, option);
        }
    }

    [HarmonyPatch(typeof(OptionsPanelsCLS), nameof(OptionsPanelsCLS.SelectOption), new Type[] { typeof(OptionsPanelsCLS.OptionName), typeof(bool) })]
    internal static class CustomLevelsOptionSelectionByNamePatch
    {
        private static void Postfix(OptionsPanelsCLS __instance, OptionsPanelsCLS.OptionName name, bool leftOptions)
        {
            if (ADOBase.sceneName == GCNS.sceneCustomLevelSelect && ADOBase.cls != null)
            {
                return;
            }

            MenuNarration.SpeakClsOptionByName(__instance, name, leftOptions);
        }
    }

    [HarmonyPatch(typeof(OptionsPanelsCLS), "TogglePanel")]
    internal static class CustomLevelsPanelTogglePatch
    {
        private static void Postfix(bool left, bool show)
        {
            if (ADOBase.sceneName == GCNS.sceneCustomLevelSelect && ADOBase.cls != null)
            {
                return;
            }

            MenuNarration.SpeakCustomLevelsPanel(left, show);
        }
    }
}
