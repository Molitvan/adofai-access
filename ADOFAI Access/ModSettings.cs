using System;
using System.IO;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace ADOFAI_Access
{
    internal enum PlayMode
    {
        Vanilla = 0,
        PatternPreview = 1,
        ListenRepeat = 2
    }

    internal enum ListenRepeatStartEndCueMode
    {
        None = 0,
        Speech = 1,
        Sound = 2,
        Both = 3
    }

    internal sealed class ModSettingsData
    {
        public bool menuNarrationEnabled = true;
        public PlayMode playMode = PlayMode.Vanilla;
        public int patternPreviewBeatsAhead = 4;
        public bool listenRepeatAudioDuckingEnabled = true;
        public ListenRepeatStartEndCueMode listenRepeatStartEndCueMode = ListenRepeatStartEndCueMode.Sound;
    }

    internal static class ModSettings
    {
        private static readonly object Sync = new object();
        private static bool _loaded;
        private static ModSettingsData _current = new ModSettingsData();

        public static ModSettingsData Current
        {
            get
            {
                EnsureLoaded();
                return _current;
            }
        }

        public static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded)
                {
                    return;
                }

                _loaded = true;
                try
                {
                    string path = GetSettingsPath();
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        ModSettingsData parsed = JsonConvert.DeserializeObject<ModSettingsData>(json);
                        if (parsed != null)
                        {
                            _current = parsed;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[ADOFAI Access] Failed to load settings: {ex}");
                }

                Sanitize();
                Save();
            }
        }

        public static void Save()
        {
            lock (Sync)
            {
                try
                {
                    Sanitize();
                    string path = GetSettingsPath();
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    string json = JsonConvert.SerializeObject(_current, Formatting.Indented);
                    File.WriteAllText(path, json);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[ADOFAI Access] Failed to save settings: {ex}");
                }
            }
        }

        private static void Sanitize()
        {
            if (_current == null)
            {
                _current = new ModSettingsData();
            }

            if (_current.patternPreviewBeatsAhead <= 0)
            {
                _current.patternPreviewBeatsAhead = 4;
            }
            else if (_current.patternPreviewBeatsAhead > 16)
            {
                _current.patternPreviewBeatsAhead = 16;
            }

            if (!Enum.IsDefined(typeof(PlayMode), _current.playMode))
            {
                _current.playMode = PlayMode.Vanilla;
            }

            if (!Enum.IsDefined(typeof(ListenRepeatStartEndCueMode), _current.listenRepeatStartEndCueMode))
            {
                _current.listenRepeatStartEndCueMode = ListenRepeatStartEndCueMode.Sound;
            }
        }

        private static string GetSettingsPath()
        {
            string gameRoot = GetGameRoot();
            return Path.Combine(gameRoot, "UserData", "ADOFAI_Access", "settings.json");
        }

        private static string GetGameRoot()
        {
            if (!string.IsNullOrEmpty(Application.dataPath))
            {
                string root = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrEmpty(root))
                {
                    return root;
                }
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
