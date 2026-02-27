using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ADOFAI;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class AccessibleLevelSelectMenu
    {
        private const KeyCode ToggleKey = KeyCode.F6;
        private const KeyCode ExecuteKey = KeyCode.Return;
        private const KeyCode ExecuteKeyAlt = KeyCode.Space;

        private static readonly List<MenuEntry> Entries = new List<MenuEntry>();
        private static bool _isOpen;
        private static bool _restoreResponsive;
        private static int _selectedIndex;
        private static bool _openHintSpokenLevelSelect;
        private static bool _openHintSpokenClsInitial;
        private static bool _openHintSpokenClsBrowse;
        private static bool _openHintSpokenGameplayStart;
        private static bool _openHintSpokenNeoCosmos;
        private static MenuContext _openContext;
        private static readonly FieldInfo ClsSelectedLevelKeyField = typeof(scnCLS).GetField("levelToSelect", BindingFlags.Instance | BindingFlags.NonPublic);
        public static bool IsOpen => _isOpen;
        public static void CloseFromExternal(bool speak = false) => Close(speak);

        private enum MenuContext
        {
            None = 0,
            LevelSelect = 1,
            CustomLevelsInitial = 2,
            CustomLevelsBrowse = 3,
            GameplayPressStart = 4,
            NeoCosmosMap = 5
        }

        private sealed class MenuEntry
        {
            public string Label;
            public Action Execute;
            public bool Locked;
        }

        public static void Tick()
        {
            if (AccessSettingsMenu.IsOpen)
            {
                return;
            }

            MenuContext context = GetCurrentContext();
            if (context == MenuContext.None)
            {
                _openHintSpokenLevelSelect = false;
                _openHintSpokenClsInitial = false;
                _openHintSpokenClsBrowse = false;
                _openHintSpokenGameplayStart = false;
                _openHintSpokenNeoCosmos = false;

                if (_isOpen)
                {
                    Close(speak: false);
                }

                return;
            }

            if (context == MenuContext.LevelSelect && !_openHintSpokenLevelSelect)
            {
                _openHintSpokenLevelSelect = true;
                MenuNarration.Speak("Press F6 to open accessible menu", interrupt: false);
            }
            else if (context == MenuContext.CustomLevelsInitial && !_openHintSpokenClsInitial)
            {
                _openHintSpokenClsInitial = true;
                MenuNarration.Speak("Press F6 to open accessible menu", interrupt: false);
            }
            else if (context == MenuContext.CustomLevelsBrowse && !_openHintSpokenClsBrowse)
            {
                _openHintSpokenClsBrowse = true;
                MenuNarration.Speak("Press F6 to open accessible menu", interrupt: false);
            }
            else if (context == MenuContext.GameplayPressStart && !_openHintSpokenGameplayStart)
            {
                _openHintSpokenGameplayStart = true;
                MenuNarration.Speak("Press F6 to open accessible menu", interrupt: false);
            }
            else if (context == MenuContext.NeoCosmosMap && !_openHintSpokenNeoCosmos)
            {
                _openHintSpokenNeoCosmos = true;
                MenuNarration.Speak("Press F6 to open accessible menu", interrupt: false);
            }

            if (_isOpen && context != _openContext)
            {
                Close(speak: false);
                return;
            }

            if (Input.GetKeyDown(ToggleKey))
            {
                if (_isOpen)
                {
                    Close(speak: true);
                }
                else
                {
                    Open();
                }

                return;
            }

            if (!_isOpen)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CustomMenuInputGuard.SuppressPauseForFrames();
                Close(speak: true);
                return;
            }

            if (Entries.Count == 0)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                MoveSelection(-1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                MoveSelection(1);
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
                _selectedIndex = Entries.Count - 1;
                SpeakSelection();
                return;
            }

            if (Input.GetKeyDown(ExecuteKey) || Input.GetKeyDown(ExecuteKeyAlt))
            {
                ExecuteSelection();
            }
        }

        private static void Open()
        {
            if (AccessSettingsMenu.IsOpen)
            {
                AccessSettingsMenu.CloseFromExternal(speak: false);
            }

            MenuContext context = GetCurrentContext();
            BuildEntries(context);
            if (Entries.Count == 0)
            {
                MenuNarration.Speak("Accessible menu unavailable here", interrupt: true);
                return;
            }

            _isOpen = true;
            _openContext = context;
            _selectedIndex = 0;

            scrController controller = ADOBase.controller;
            if (controller != null)
            {
                _restoreResponsive = controller.responsive;
                controller.responsive = false;
            }
            else
            {
                _restoreResponsive = false;
            }

            MenuNarration.Speak("Accessible menu open. Up and down to navigate. Enter to activate. Escape to close.", interrupt: true);
            SpeakSelection();
        }

        private static void Close(bool speak)
        {
            _isOpen = false;
            Entries.Clear();
            _openContext = MenuContext.None;

            scrController controller = ADOBase.controller;
            if (controller != null)
            {
                controller.responsive = _restoreResponsive;
            }

            _restoreResponsive = false;

            if (speak)
            {
                MenuNarration.Speak("Accessible menu closed", interrupt: true);
            }
        }

        private static void MoveSelection(int delta)
        {
            if (Entries.Count == 0)
            {
                return;
            }

            _selectedIndex += delta;
            if (_selectedIndex < 0)
            {
                _selectedIndex = Entries.Count - 1;
            }
            else if (_selectedIndex >= Entries.Count)
            {
                _selectedIndex = 0;
            }

            SpeakSelection();
        }

        private static void SpeakSelection()
        {
            if (_selectedIndex < 0 || _selectedIndex >= Entries.Count)
            {
                return;
            }

            MenuEntry selected = Entries[_selectedIndex];
            string lockedSuffix = selected.Locked ? ", locked" : string.Empty;
            MenuNarration.Speak($"{selected.Label}{lockedSuffix}, {_selectedIndex + 1} of {Entries.Count}", interrupt: true);
        }

        private static void ExecuteSelection()
        {
            if (_selectedIndex < 0 || _selectedIndex >= Entries.Count)
            {
                return;
            }

            MenuEntry selected = Entries[_selectedIndex];
            if (selected.Locked)
            {
                MenuNarration.Speak($"{selected.Label}, locked", interrupt: true);
                return;
            }

            selected.Execute?.Invoke();
        }

        private static void BuildEntries(MenuContext context)
        {
            Entries.Clear();
            if (context == MenuContext.LevelSelect)
            {
                BuildLevelSelectEntries();
            }
            else if (context == MenuContext.CustomLevelsInitial)
            {
                BuildCustomLevelsInitialEntries();
            }
            else if (context == MenuContext.CustomLevelsBrowse)
            {
                BuildCustomLevelsBrowseEntries();
            }
            else if (context == MenuContext.GameplayPressStart)
            {
                BuildGameplayPressStartEntries();
            }
            else if (context == MenuContext.NeoCosmosMap)
            {
                BuildNeoCosmosEntries();
            }
        }

        private static void BuildLevelSelectEntries()
        {
            AddEntry("Open settings", () =>
            {
                Close(speak: false);
                scrController controller = ADOBase.controller;
                if (controller?.takeScreenshot != null)
                {
                    MenuNarration.Speak("Opening settings", interrupt: true);
                    controller.takeScreenshot.ShowPauseMenu(goToSettings: true);
                }
            });

            AddEntry("Open pause menu", () =>
            {
                Close(speak: false);
                scrController controller = ADOBase.controller;
                if (controller?.takeScreenshot != null)
                {
                    MenuNarration.Speak("Opening pause menu", interrupt: true);
                    controller.takeScreenshot.ShowPauseMenu(goToSettings: false);
                }
            });

            AddEntry("Open calibration", () =>
            {
                Close(speak: false);
                MenuNarration.Speak("Opening calibration", interrupt: true);
                ADOBase.GoToCalibration();
            });

            if (SteamIntegration.initialized)
            {
                AddEntry("Open custom levels", () =>
                {
                    Close(speak: false);
                    MenuNarration.Speak("Opening custom levels", interrupt: true);
                    ADOBase.controller?.PortalTravelAction(Portal.CustomLevelsScene);
                });
            }

            AddEntry("Open level editor", () =>
            {
                Close(speak: false);
                MenuNarration.Speak("Opening level editor", interrupt: true);
                ADOBase.controller?.PortalTravelAction(Portal.EditorScene);
            });

            if (!GCS.FOOL_JOKER && NeoCosmosManager.instance != null && NeoCosmosManager.instance.installed)
            {
                AddEntry("Open Neo Cosmos map", () =>
                {
                    Close(speak: false);
                    MenuNarration.Speak("Opening Neo Cosmos map", interrupt: true);
                    ADOBase.controller?.PortalTravelAction(Portal.TaroDLCMap);
                });
            }

            if (!GCS.FOOL_JOKER && VegaDLCManager.instance != null && VegaDLCManager.instance.installed)
            {
                AddEntry("Open Vega map", () =>
                {
                    Close(speak: false);
                    MenuNarration.Speak("Opening Vega map", interrupt: true);
                    ADOBase.controller?.PortalTravelAction(Portal.VegaDLCMap);
                });
            }

            IEnumerable<scrPortal> visiblePortals = scrPortal.portals.Values
                .Where(p => p != null)
                .Where(p => p.gameObject != null && p.gameObject.activeInHierarchy)
                .Where(p => !p.hidden)
                .OrderBy(p => WorldSortKey(p.world))
                .ThenBy(p => p.world, StringComparer.Ordinal);

            foreach (scrPortal portal in visiblePortals)
            {
                string worldId = portal.world;
                bool locked = portal.locked || !IsWorldReachableByProgress(worldId);
                string label = $"Enter world {DisplayWorldId(worldId)}";
                string targetWorld = worldId;
                AddEntry(label, () =>
                {
                    Close(speak: false);
                    MenuNarration.Speak($"Entering world {DisplayWorldId(targetWorld)}", interrupt: true);
                    ADOBase.controller?.EnterWorld(targetWorld);
                }, locked);
            }
        }

        private static void BuildCustomLevelsInitialEntries()
        {
            AddEntry("Open workshop levels", () =>
            {
                Close(speak: false);
                scnCLS cls = ADOBase.cls;
                if (cls != null)
                {
                    MenuNarration.Speak("Opening workshop levels", interrupt: true);
                    cls.WorkshopLevelsPortal();
                }
            });

            AddEntry("Open featured levels", () =>
            {
                Close(speak: false);
                scnCLS cls = ADOBase.cls;
                if (cls != null)
                {
                    MenuNarration.Speak("Opening featured levels", interrupt: true);
                    cls.FeaturedLevelsPortal();
                }
            });

            AddEntry("Open tech featured levels", () =>
            {
                Close(speak: false);
                scnCLS cls = ADOBase.cls;
                if (cls != null)
                {
                    MenuNarration.Speak("Opening tech featured levels", interrupt: true);
                    cls.TechFeaturedLevelsPortal();
                }
            });

            AddEntry("Quit to main menu", () =>
            {
                Close(speak: false);
                scnCLS cls = ADOBase.cls;
                if (cls != null)
                {
                    MenuNarration.Speak("Quitting to main menu", interrupt: true);
                    cls.QuitPortal();
                }
            });
        }

        private static void BuildCustomLevelsBrowseEntries()
        {
            AddEntry("Play selected", () =>
            {
                scnCLS cls = ADOBase.cls;
                if (cls == null)
                {
                    MenuNarration.Speak("Unavailable", interrupt: true);
                    return;
                }

                if (!TryGetSelectedCustomLevel(cls, out _, out _, out _))
                {
                    cls.SearchLevels(cls.searchParameter);
                }

                if (!TryGetSelectedCustomLevel(cls, out _, out GenericDataCLS selectedData, out bool deleted))
                {
                    MenuNarration.Speak("No level selected", interrupt: true);
                    return;
                }

                if (deleted)
                {
                    MenuNarration.Speak("Selected level unavailable", interrupt: true);
                    return;
                }

                bool notDownloaded = cls.downloadText != null && cls.downloadText.enabled;
                string actionText = selectedData != null && selectedData.isFolder
                    ? "Opening folder"
                    : (notDownloaded ? "Not downloaded. Starting download." : "Starting level");
                Close(speak: false);
                MenuNarration.Speak(actionText, interrupt: true);
                cls.EnterLevel();
            });

            AddEntry("Read level info", () =>
            {
                if (!TryGetSelectedCustomLevel(ADOBase.cls, out _, out GenericDataCLS selectedData, out _))
                {
                    MenuNarration.Speak("No level selected", interrupt: true);
                    return;
                }

                string title = NormalizeForSpeech(selectedData.title);
                string artist = NormalizeForSpeech(selectedData.artist);
                string author = NormalizeForSpeech(selectedData.author);
                string difficulty = selectedData.difficulty > 0 ? selectedData.difficulty.ToString() : "unknown";
                MenuNarration.Speak($"Title {BestOf(title, "Unknown")}. Artist {BestOf(artist, "Unknown")}. Author {BestOf(author, "Unknown")}. Difficulty {difficulty}.", interrupt: true);
            });

            AddEntry("Read description", () =>
            {
                scnCLS cls = ADOBase.cls;
                if (cls == null)
                {
                    return;
                }

                string description = NormalizeForSpeech(cls.portalDescription != null ? cls.portalDescription.text : string.Empty);
                if (string.IsNullOrWhiteSpace(description))
                {
                    MenuNarration.Speak("No description", interrupt: true);
                    return;
                }

                MenuNarration.Speak(description, interrupt: true);
            });

            AddEntry("Read stats", () =>
            {
                scnCLS cls = ADOBase.cls;
                if (cls == null)
                {
                    return;
                }

                string stats = NormalizeForSpeech(cls.portalStats != null ? cls.portalStats.text : string.Empty);
                if (string.IsNullOrWhiteSpace(stats))
                {
                    MenuNarration.Speak("No stats available", interrupt: true);
                    return;
                }

                MenuNarration.Speak(stats, interrupt: true);
            });

            AddEntry("Read tags", () =>
            {
                if (!TryGetSelectedCustomLevel(ADOBase.cls, out _, out GenericDataCLS selectedData, out _))
                {
                    MenuNarration.Speak("No level selected", interrupt: true);
                    return;
                }

                string[] tags = selectedData.tags ?? Array.Empty<string>();
                string combined = string.Join(", ", tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(NormalizeForSpeech));
                MenuNarration.Speak(string.IsNullOrWhiteSpace(combined) ? "No tags" : combined, interrupt: true);
            });

            AddEntry("Toggle speed trial", () =>
            {
                if (!TryToggleClsOption(OptionsPanelsCLS.OptionName.SpeedTrial, out bool enabled))
                {
                    MenuNarration.Speak("Unavailable", interrupt: true);
                    return;
                }

                MenuNarration.Speak(enabled ? "Speed trial on" : "Speed trial off", interrupt: true);
            });

            AddEntry("Toggle no fail", () =>
            {
                if (!TryToggleClsOption(OptionsPanelsCLS.OptionName.NoFail, out bool enabled))
                {
                    MenuNarration.Speak("Unavailable", interrupt: true);
                    return;
                }

                MenuNarration.Speak(enabled ? "No fail on" : "No fail off", interrupt: true);
            });

            AddEntry("Toggle unlock key limiter", () =>
            {
                if (!TryToggleClsOption(OptionsPanelsCLS.OptionName.UnlockKeyLimiter, out bool enabled))
                {
                    MenuNarration.Speak("Unavailable", interrupt: true);
                    return;
                }

                MenuNarration.Speak(enabled ? "Unlock key limiter on" : "Unlock key limiter off", interrupt: true);
            });

            AddEntry("Sort by difficulty", () => ApplyClsSort(OptionsPanelsCLS.OptionName.Difficulty, "difficulty"));
            AddEntry("Sort by last played", () => ApplyClsSort(OptionsPanelsCLS.OptionName.LastPlayed, "last played"));
            AddEntry("Sort by song", () => ApplyClsSort(OptionsPanelsCLS.OptionName.Song, "song"));
            AddEntry("Sort by artist", () => ApplyClsSort(OptionsPanelsCLS.OptionName.Artist, "artist"));
            AddEntry("Sort by author", () => ApplyClsSort(OptionsPanelsCLS.OptionName.Author, "author"));

            AddEntry("Find", () =>
            {
                scnCLS cls = ADOBase.cls;
                if (cls == null || cls.optionsPanels == null)
                {
                    MenuNarration.Speak("Unavailable", interrupt: true);
                    return;
                }

                cls.ToggleSearchMode(search: true);
                string query = NormalizeForSpeech(cls.optionsPanels.searchInputField != null ? cls.optionsPanels.searchInputField.text : string.Empty);
                string value = string.IsNullOrWhiteSpace(query) ? "empty" : query;
                MenuNarration.Speak($"Find, text field, {value}", interrupt: true);
            });

            AddEntry("Clear search", () =>
            {
                scnCLS cls = ADOBase.cls;
                if (cls == null || cls.optionsPanels == null || cls.optionsPanels.searchInputField == null)
                {
                    MenuNarration.Speak("Unavailable", interrupt: true);
                    return;
                }

                cls.optionsPanels.searchInputField.text = string.Empty;
                cls.SearchLevels(string.Empty);
                MenuNarration.Speak("Search cleared", interrupt: true);
            });
        }

        private static void BuildGameplayPressStartEntries()
        {
            scrController controller = ADOBase.controller;
            scnGame scene = ADOBase.customLevel;
            bool hasLevelData = scene != null && scene.levelData != null;
            if (controller == null)
            {
                AddEntry("Read level stats", () => MenuNarration.Speak("Level stats unavailable", interrupt: true), locked: true);
                return;
            }

            string levelName = BestOf(controller.levelName, BestOf(ADOBase.currentLevel, GCS.internalLevelName));
            string localizedTitle = !string.IsNullOrWhiteSpace(levelName) ? NormalizeForSpeech(ADOBase.GetLocalizedLevelName(levelName)) : string.Empty;
            string artist = hasLevelData ? NormalizeForSpeech(scene.levelData.artist) : string.Empty;
            string song = hasLevelData ? NormalizeForSpeech(scene.levelData.song) : string.Empty;
            string author = hasLevelData ? NormalizeForSpeech(scene.levelData.author) : string.Empty;
            string hash = hasLevelData ? scene.levelData.Hash : string.Empty;
            string worldKey = scrController.currentWorldString;
            bool hasOfficialWorld = ADOBase.isOfficialLevel && !string.IsNullOrWhiteSpace(worldKey) && GCNS.worldData.ContainsKey(worldKey);
            bool hasCustomStats = !ADOBase.isOfficialLevel && !string.IsNullOrWhiteSpace(hash);

            AddEntry("Toggle speed trial", () =>
            {
                bool enable = !GCS.speedTrialMode;

                // For official levels on press-to-start, re-entering the same level
                // is required to switch between normal and speed-trial variants.
                if (ADOBase.isOfficialLevel && controller != null && !string.IsNullOrWhiteSpace(controller.levelName))
                {
                    Close(speak: false);
                    MenuNarration.Speak(enable ? "Switching to speed trial" : "Switching to normal level", interrupt: true);
                    controller.EnterLevel(controller.levelName, speedTrial: enable);
                    return;
                }

                // Fallback path for non-official contexts where EnterLevel routing
                // is not appropriate.
                GCS.speedTrialMode = enable;
                if (enable)
                {
                    if (GCS.nextSpeedRun < 1.1f)
                    {
                        GCS.nextSpeedRun = 1.1f;
                    }

                    if (GCS.currentSpeedTrial < 1f)
                    {
                        GCS.currentSpeedTrial = 1f;
                    }
                }
                else
                {
                    GCS.currentSpeedTrial = 1f;
                    GCS.nextSpeedRun = 1f;
                }

                MenuNarration.Speak(enable ? "Speed trial on" : "Speed trial off", interrupt: true);
            });

            AddEntry("Read level name", () =>
            {
                string idPart = string.IsNullOrWhiteSpace(levelName) ? string.Empty : levelName;
                string titlePart = string.IsNullOrWhiteSpace(localizedTitle) ? string.Empty : localizedTitle;
                string text = string.IsNullOrWhiteSpace(titlePart) || string.Equals(titlePart, idPart, StringComparison.OrdinalIgnoreCase)
                    ? $"Level {BestOf(idPart, "unknown")}."
                    : $"Level {BestOf(idPart, "unknown")}. {titlePart}.";
                MenuNarration.Speak(text, interrupt: true);
            });

            if (hasLevelData && (!string.IsNullOrWhiteSpace(song) || !string.IsNullOrWhiteSpace(artist) || !string.IsNullOrWhiteSpace(author)))
            {
                AddEntry("Read song info", () =>
                {
                    string artistPart = string.IsNullOrWhiteSpace(artist) ? "Unknown" : artist;
                    string songPart = string.IsNullOrWhiteSpace(song) ? "Unknown" : song;
                    string authorPart = string.IsNullOrWhiteSpace(author) ? "Unknown" : author;
                    MenuNarration.Speak($"Song {songPart}. Artist {artistPart}. Author {authorPart}.", interrupt: true);
                });
            }

            AddEntry("Read selected difficulty", () =>
            {
                string difficulty = RDString.Get("enum.Difficulty." + GCS.difficulty);
                if (string.IsNullOrWhiteSpace(difficulty))
                {
                    difficulty = GCS.difficulty.ToString();
                }

                MenuNarration.Speak("Difficulty " + NormalizeForSpeech(difficulty), interrupt: true);
            });

            if (hasOfficialWorld)
            {
                int worldIndex = scrController.currentWorld;
                float completion = Persistence.GetPercentCompletion(worldIndex);
                float bestAcc = Persistence.GetBestPercentAccuracy(worldIndex);
                float bestXAcc = Persistence.GetBestPercentXAccuracy(worldIndex);
                float bestSpeed = Persistence.GetBestSpeedMultiplier(worldIndex);

                AddEntry("Read best record summary", () =>
                {
                    List<string> parts = new List<string>
                    {
                        "Completion " + FormatPercentStat(completion),
                        "Accuracy " + FormatPercentStat(bestAcc),
                        "X accuracy " + FormatPercentStat(bestXAcc)
                    };

                    string speedText = FormatSpeedTrialStat(bestSpeed);
                    if (!string.Equals(speedText, "none recorded", StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add("Speed Trial " + speedText);
                    }

                    MenuNarration.Speak(string.Join(". ", parts) + ".", interrupt: true);
                });
                AddEntry("Read completion", () => SpeakNamedStat("Completion", FormatPercentStat(Persistence.GetPercentCompletion(worldIndex))));
                AddEntry("Read best accuracy", () => SpeakNamedStat("Best accuracy", FormatPercentStat(Persistence.GetBestPercentAccuracy(worldIndex))));
                AddEntry("Read best X accuracy", () => SpeakNamedStat("Best X accuracy", FormatPercentStat(Persistence.GetBestPercentXAccuracy(worldIndex))));
                AddEntry("Read best speed trial", () => SpeakNamedStat("Best speed trial", FormatSpeedTrialStat(Persistence.GetBestSpeedMultiplier(worldIndex))));
                return;
            }

            if (hasCustomStats)
            {
                string customHash = hash;
                float completion = Persistence.GetCustomWorldCompletion(customHash);
                float bestAcc = Persistence.GetCustomWorldAccuracy(customHash);
                float bestXAcc = Persistence.GetCustomWorldXAccuracy(customHash);
                float bestSpeed = Persistence.GetCustomWorldSpeedTrial(customHash);

                AddEntry("Read best record summary", () =>
                {
                    List<string> parts = new List<string>
                    {
                        "Completion " + FormatPercentStat(completion),
                        "Accuracy " + FormatPercentStat(bestAcc),
                        "X accuracy " + FormatPercentStat(bestXAcc)
                    };

                    string speedText = FormatSpeedTrialStat(bestSpeed);
                    if (!string.Equals(speedText, "none recorded", StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add("Speed Trial " + speedText);
                    }

                    MenuNarration.Speak(string.Join(". ", parts) + ".", interrupt: true);
                });
                AddEntry("Read completion", () => SpeakNamedStat("Completion", FormatPercentStat(Persistence.GetCustomWorldCompletion(customHash))));
                AddEntry("Read best accuracy", () => SpeakNamedStat("Best accuracy", FormatPercentStat(Persistence.GetCustomWorldAccuracy(customHash))));
                AddEntry("Read best X accuracy", () => SpeakNamedStat("Best X accuracy", FormatPercentStat(Persistence.GetCustomWorldXAccuracy(customHash))));
                AddEntry("Read best speed trial", () => SpeakNamedStat("Best speed trial", FormatSpeedTrialStat(Persistence.GetCustomWorldSpeedTrial(customHash))));
                return;
            }

            AddEntry("Read best record summary", () => MenuNarration.Speak("No saved records yet", interrupt: true));
        }

        private static void BuildNeoCosmosEntries()
        {
            AddEntry("Exit Neo Cosmos", () =>
            {
                Close(speak: false);
                MenuNarration.Speak("Exiting Neo Cosmos", interrupt: true);
                ADOBase.controller?.PortalTravelAction(Portal.TaroDLCMapExit);
            });

            if (ADOBase.sceneName == GCNS.sceneTaroMenu2 && Persistence.IsWorldComplete("T4"))
            {
                AddEntry("Open puzzle room 1", () =>
                {
                    Close(speak: false);
                    MenuNarration.Speak("Opening puzzle room 1", interrupt: true);
                    ADOBase.controller?.PortalTravelAction(Portal.Puzzle1);
                });
            }

            if (ADOBase.sceneName == GCNS.sceneTaroMenu0 && Persistence.IsWorldComplete("T5"))
            {
                AddEntry("Play T5 X with cutscene", () =>
                {
                    Close(speak: false);
                    scnTaroMenu0 taroMenu0 = UnityEngine.Object.FindObjectOfType<scnTaroMenu0>();
                    if (taroMenu0 != null)
                    {
                        taroMenu0.EnableT5Cutscene();
                    }

                    MenuNarration.Speak("Starting T5 X with cutscene", interrupt: true);
                    ADOBase.controller?.EnterLevel("T5-X", speedTrial: false);
                });
            }

            IEnumerable<string> worlds = GetNeoCosmosMenuWorlds()
                .OrderBy(TaroWorldSortKey)
                .ThenBy(w => w, StringComparer.Ordinal);

            foreach (string worldId in worlds)
            {
                bool locked = TryGetPortal(worldId, out scrPortal portal) && portal.locked;
                string label = $"Enter world {DisplayTaroWorldId(worldId)}";
                AddEntry(label, () =>
                {
                    Close(speak: false);
                    MenuNarration.Speak($"Entering world {DisplayTaroWorldId(worldId)}", interrupt: true);
                    ADOBase.controller?.EnterWorld(worldId);
                }, locked);
            }
        }

        private static MenuContext GetCurrentContext()
        {
            if (ADOBase.sceneName == GCNS.sceneLevelSelect && ADOBase.levelSelect is scnLevelSelect)
            {
                return MenuContext.LevelSelect;
            }

            if (IsGameplayPressStartContext())
            {
                return MenuContext.GameplayPressStart;
            }

            if (ADOBase.sceneName == GCNS.sceneCustomLevelSelect && ADOBase.cls != null && ADOBase.cls.showingInitialMenu)
            {
                return MenuContext.CustomLevelsInitial;
            }

            if (ADOBase.sceneName == GCNS.sceneCustomLevelSelect && ADOBase.cls != null && !ADOBase.cls.showingInitialMenu)
            {
                return MenuContext.CustomLevelsBrowse;
            }

            if (IsNeoCosmosMapContext())
            {
                return MenuContext.NeoCosmosMap;
            }

            return MenuContext.None;
        }

        private static bool IsNeoCosmosMapContext()
        {
            string sceneName = ADOBase.sceneName;
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            return sceneName == GCNS.sceneTaroMenu0 ||
                   sceneName == GCNS.sceneTaroMenu1 ||
                   sceneName == GCNS.sceneTaroMenu2 ||
                   sceneName == GCNS.sceneTaroMenu3;
        }

        private static bool IsGameplayPressStartContext()
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

            bool gameplayLikeScene = ADOBase.sceneName == GCNS.sceneGame || ADOBase.isScnGame || ADOBase.isPlayingLevel || controller.gameworld || controller.isPuzzleRoom;
            if (!gameplayLikeScene || controller.state != States.Start)
            {
                return false;
            }

            scrUIController ui = ADOBase.uiController;
            if (ui == null || ui.txtPressToStart == null || ui.txtPressToStart.gameObject == null || !ui.txtPressToStart.gameObject.activeInHierarchy)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(ui.txtPressToStart.text);
        }

        private static IEnumerable<string> GetNeoCosmosMenuWorlds()
        {
            switch (ADOBase.sceneName)
            {
                case GCNS.sceneTaroMenu1:
                    return FilterExistingTaroWorlds("T1", "T2", "T3");
                case GCNS.sceneTaroMenu2:
                    return FilterExistingTaroWorlds("T1", "T2", "T3", "T4");
                case GCNS.sceneTaroMenu3:
                    return FilterExistingTaroWorlds("T1", "T2", "T3", "T4", "T5");
                case GCNS.sceneTaroMenu0:
                    return scrPortal.portals.Values
                        .Where(p => p != null)
                        .Select(p => p.world)
                        .Where(w => !string.IsNullOrWhiteSpace(w) && w.IsTaro())
                        .Distinct(StringComparer.Ordinal);
                default:
                    return Enumerable.Empty<string>();
            }
        }

        private static IEnumerable<string> FilterExistingTaroWorlds(params string[] worlds)
        {
            return worlds.Where(w => !string.IsNullOrWhiteSpace(w) && GCNS.worldData.ContainsKey(w));
        }

        private static bool TryGetPortal(string worldId, out scrPortal portal)
        {
            portal = null;
            if (string.IsNullOrWhiteSpace(worldId) || scrPortal.portals == null)
            {
                return false;
            }

            if (!scrPortal.portals.TryGetValue(worldId, out portal))
            {
                portal = null;
                return false;
            }

            return portal != null;
        }

        private static bool TryGetSelectedCustomLevel(scnCLS cls, out string levelKey, out GenericDataCLS selectedData, out bool deleted)
        {
            levelKey = null;
            selectedData = null;
            deleted = false;
            if (cls == null)
            {
                return false;
            }

            levelKey = ClsSelectedLevelKeyField?.GetValue(cls) as string;
            if (string.IsNullOrEmpty(levelKey) || cls.loadedLevels == null)
            {
                return false;
            }

            if (!cls.loadedLevels.TryGetValue(levelKey, out selectedData))
            {
                return false;
            }

            deleted = cls.levelDeleted;
            return true;
        }

        private static void ApplyClsSort(OptionsPanelsCLS.OptionName optionName, string spokenName)
        {
            scnCLS cls = ADOBase.cls;
            OptionsPanelsCLS panels = cls != null ? cls.optionsPanels : null;
            if (cls == null || panels == null)
            {
                MenuNarration.Speak("Unavailable", interrupt: true);
                return;
            }

            panels.sortingMethod = optionName;
            cls.sortedLevelKeys = panels.SortedLevelKeys();
            cls.SearchLevels(cls.searchParameter);
            panels.UpdateOrderText();
            MenuNarration.Speak("Sort by " + spokenName, interrupt: true);
        }

        private static bool TryToggleClsOption(OptionsPanelsCLS.OptionName optionName, out bool enabled)
        {
            enabled = false;
            scnCLS cls = ADOBase.cls;
            OptionsPanelsCLS panels = cls != null ? cls.optionsPanels : null;
            if (panels == null || panels.rightPanelOptions == null)
            {
                return false;
            }

            OptionsPanelsCLS.Option option = panels.rightPanelOptions.FirstOrDefault(o => o != null && o.name == optionName);
            if (option == null)
            {
                return false;
            }

            bool nextSelected = !option.selected;
            option.SetState(_highlighted: option.highlighted, _selected: nextSelected);
            enabled = option.selected;
            return true;
        }

        private static void SpeakNamedStat(string label, string value)
        {
            string normalizedLabel = NormalizeForSpeech(label);
            string normalizedValue = NormalizeForSpeech(value);
            if (string.IsNullOrWhiteSpace(normalizedLabel))
            {
                normalizedLabel = "Stat";
            }

            if (string.IsNullOrWhiteSpace(normalizedValue))
            {
                normalizedValue = "unavailable";
            }

            MenuNarration.Speak($"{normalizedLabel}: {normalizedValue}", interrupt: true);
        }

        private static string FormatPercentStat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "unavailable";
            }

            return (value * 100f).ToString("0.##", CultureInfo.InvariantCulture) + " percent";
        }

        private static string FormatSpeedTrialStat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "unavailable";
            }

            if (value <= 0f)
            {
                return "none recorded";
            }

            return value.ToString("0.##", CultureInfo.InvariantCulture) + " x";
        }

        private static string NormalizeForSpeech(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return RDUtils.RemoveRichTags(value).Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        private static string BestOf(string primary, string fallback)
        {
            return string.IsNullOrWhiteSpace(primary) ? fallback : primary;
        }

        private static bool IsWorldReachableByProgress(string world)
        {
            if (string.IsNullOrEmpty(world))
            {
                return false;
            }

            if (ADOBase.controller == null)
            {
                return false;
            }

            if (ADOBase.levelSelect == null)
            {
                return false;
            }

            if (ADOBase.levelSelect.dlcManagers.Any(x => x.IsDLCLevel(world)))
            {
                return false;
            }

            bool isMainLateWorld = world == "7" || world == "8" || world == "9" || world == "10" || world == "11" || world == "12" || world == "B";
            bool isCrownWorld = world.IsCrownWorld();
            bool isMuseDashWorld = world.IsMuseDashWorld();
            int overallProgressStage = Persistence.GetOverallProgressStage();

            if ((overallProgressStage < 3 && world == "6") || (overallProgressStage < 5 && isMainLateWorld))
            {
                return false;
            }

            bool isXtraOrMuseDash = world.IsXtra() || isMuseDashWorld;
            if (overallProgressStage < 5 && isXtraOrMuseDash && !isCrownWorld && !isMuseDashWorld)
            {
                return false;
            }

            return true;
        }

        private static void AddEntry(string label, Action execute, bool locked = false)
        {
            Entries.Add(new MenuEntry
            {
                Label = label,
                Execute = execute,
                Locked = locked
            });
        }

        private static int WorldSortKey(string worldId)
        {
            if (int.TryParse(worldId, out int numeric))
            {
                return numeric;
            }

            if (worldId == "B")
            {
                return 100;
            }

            if (worldId.StartsWith("X", StringComparison.OrdinalIgnoreCase))
            {
                return 200;
            }

            if (worldId.StartsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                return 300;
            }

            if (worldId.StartsWith("T", StringComparison.OrdinalIgnoreCase))
            {
                return 400;
            }

            return 500;
        }

        private static int TaroWorldSortKey(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId))
            {
                return int.MaxValue;
            }

            if (string.Equals(worldId, "TP", StringComparison.OrdinalIgnoreCase))
            {
                return 1000;
            }

            int numeric = 0;
            int index = 1;
            while (index < worldId.Length && char.IsDigit(worldId[index]))
            {
                numeric = (numeric * 10) + (worldId[index] - '0');
                index++;
            }

            int suffix = worldId.EndsWith("EX", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            return numeric * 10 + suffix;
        }

        private static string DisplayTaroWorldId(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId))
            {
                return worldId;
            }

            if (worldId.EndsWith("EX", StringComparison.OrdinalIgnoreCase) && worldId.Length > 2)
            {
                return worldId.Substring(0, worldId.Length - 2) + " EX";
            }

            return worldId;
        }

        private static string DisplayWorldId(string worldId)
        {
            if (string.IsNullOrEmpty(worldId))
            {
                return worldId;
            }

            if (worldId.EndsWith("J", StringComparison.OrdinalIgnoreCase) && worldId.Length > 1)
            {
                return worldId.Substring(0, worldId.Length - 1);
            }

            return worldId;
        }
    }
}
