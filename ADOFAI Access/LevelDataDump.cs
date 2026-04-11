using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace ADOFAI_Access
{
    internal static class LevelDataDump
    {
        private const KeyCode DumpHotkey = KeyCode.F7;

        private static string _lastDumpSignature = string.Empty;
        private static float _lastDumpAt;
        private static string _lastRuntimeSignature = string.Empty;
        private static float _lastRuntimeDumpAt;

        public static void Tick()
        {
            if (AccessSettingsMenu.IsOpen)
            {
                return;
            }

            if (!Input.GetKeyDown(DumpHotkey))
            {
                return;
            }

            scnGame scene = ADOBase.customLevel;
            if (scene != null && scene.levelData != null)
            {
                if (DumpOnLevelLoad(scene.levelPath, scene, out string path))
                {
                    Announce("Level data dumped");
                    MelonLogger.Msg($"[ADOFAI Access] Manual level dump saved: {path}");
                }
                else
                {
                    Announce("Level data dump failed");
                }

                return;
            }

            if (DumpFromRuntime("manual-hotkey", out string runtimePath))
            {
                Announce("Runtime level data dumped");
                MelonLogger.Msg($"[ADOFAI Access] Manual runtime dump saved: {runtimePath}");
                return;
            }

            Announce("No level data available to dump");
        }

        private static bool DumpOnLevelLoad(string levelPath, scnGame scene, out string outputPath)
        {
            outputPath = string.Empty;

            if (scene == null || scene.levelData == null)
            {
                return false;
            }

            string levelName = BestOf(ADOBase.currentLevel, GCS.internalLevelName, Path.GetFileNameWithoutExtension(levelPath), "unknown-level");
            string signature = $"{levelName}|{scene.levelData.Hash}|{BestOf(levelPath)}";
            float now = Time.unscaledTime;
            if (string.Equals(_lastDumpSignature, signature, StringComparison.Ordinal) && now - _lastDumpAt < 0.1f)
            {
                return false;
            }

            _lastDumpSignature = signature;
            _lastDumpAt = now;

            try
            {
                string dumpDirectory = GetDumpDirectory();
                Directory.CreateDirectory(dumpDirectory);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                string filename = $"{timestamp}_{SanitizeFilename(levelName)}.json";
                outputPath = Path.Combine(dumpDirectory, filename);

                object payload = BuildPayload(levelPath, scene, levelName);
                string json = ToJson(payload);
                File.WriteAllText(outputPath, json);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ADOFAI Access] Failed level dump: {ex}");
                return false;
            }
        }

        private static bool DumpFromRuntime(string trigger, out string outputPath)
        {
            outputPath = string.Empty;

            try
            {
                scrController controller = ADOBase.controller;
                if (controller == null)
                {
                    return false;
                }

                string levelName = BestOf(controller.levelName, ADOBase.currentLevel, GCS.internalLevelName, "unknown-level");
                int floorCount = ADOBase.lm != null && ADOBase.lm.listFloors != null ? ADOBase.lm.listFloors.Count : 0;
                string signature = $"{levelName}|{ADOBase.sceneName}|{floorCount}";
                float now = Time.unscaledTime;
                if (string.Equals(signature, _lastRuntimeSignature, StringComparison.Ordinal) && now - _lastRuntimeDumpAt < 0.1f)
                {
                    return false;
                }

                _lastRuntimeSignature = signature;
                _lastRuntimeDumpAt = now;

                string dumpDirectory = GetDumpDirectory();
                Directory.CreateDirectory(dumpDirectory);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                string filename = $"{timestamp}_{SanitizeFilename(levelName)}_runtime.json";
                outputPath = Path.Combine(dumpDirectory, filename);

                Dictionary<string, object> payload = new Dictionary<string, object>
                {
                    ["dumpVersion"] = 2,
                    ["trigger"] = trigger,
                    ["timestampUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ["sceneName"] = ADOBase.sceneName,
                    ["levelName"] = levelName,
                    ["worldKey"] = scrController.currentWorldString,
                    ["isBossLevel"] = controller.isbosslevel,
                    ["isPuzzleRoom"] = controller.isPuzzleRoom,
                    ["gameworld"] = controller.gameworld,
                    ["speedTrialMode"] = GCS.speedTrialMode,
                    ["currentSpeedTrial"] = GCS.currentSpeedTrial,
                    ["practiceMode"] = GCS.practiceMode,
                    ["checkpointNum"] = GCS.checkpointNum,
                    ["conductor"] = ADOBase.conductor == null ? null : new Dictionary<string, object>
                    {
                        ["bpm"] = ADOBase.conductor.bpm,
                        ["addOffset"] = ADOBase.conductor.addoffset,
                        ["countdownTicks"] = ADOBase.conductor.countdownTicks,
                        ["pitch"] = ADOBase.conductor.song != null ? ADOBase.conductor.song.pitch : 0f,
                        ["volume"] = ADOBase.conductor.song != null ? ADOBase.conductor.song.volume : 0f
                    },
                    ["runtimeFloors"] = BuildFloors(ADOBase.lm != null ? ADOBase.lm.listFloors : null)
                };

                scnGame scene = ADOBase.customLevel;
                if (scene != null && scene.levelData != null)
                {
                    payload["levelPath"] = scene.levelPath;
                    payload["levelData"] = BuildLevelData(scene);
                }

                string json = ToJson(payload);
                File.WriteAllText(outputPath, json);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ADOFAI Access] Failed runtime level dump: {ex}");
                return false;
            }
        }

        private static void Announce(string text)
        {
            try
            {
                MenuNarration.Speak(text, interrupt: true);
            }
            catch
            {
                PrismSpeech.Output(text, true);
            }
        }

        private static string GetDumpDirectory()
        {
            string gameRoot;
            if (!string.IsNullOrEmpty(Application.dataPath))
            {
                gameRoot = Path.GetDirectoryName(Application.dataPath);
            }
            else
            {
                gameRoot = AppDomain.CurrentDomain.BaseDirectory;
            }

            return Path.Combine(gameRoot, "UserData", "ADOFAI_Access", "LevelDumps");
        }

        private static object BuildPayload(string levelPath, scnGame scene, string levelName)
        {
            scrController controller = ADOBase.controller;
            List<scrFloor> floors = ADOBase.lm != null ? ADOBase.lm.listFloors : null;
            string worldKey = scrController.currentWorldString;

            object worldData = null;
            if (!string.IsNullOrEmpty(worldKey) && GCNS.worldData.ContainsKey(worldKey))
            {
                GCNS.WorldData w = GCNS.worldData[worldKey];
                worldData = new Dictionary<string, object>
                {
                    ["key"] = worldKey,
                    ["index"] = w.index,
                    ["levelCount"] = w.levelCount,
                    ["trialAim"] = w.trialAim,
                    ["hasCheckpoints"] = w.hasCheckpoints,
                    ["internalLevel"] = w.internalLevel,
                    ["isDLC"] = w.isDLC,
                    ["medalCount"] = w.medalCount,
                    ["requiredMedals"] = w.requiredMedals,
                    ["island"] = w.island.ToString()
                };
            }

            return new Dictionary<string, object>
            {
                ["dumpVersion"] = 1,
                ["timestampUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["source"] = new Dictionary<string, object>
                {
                    ["sceneName"] = ADOBase.sceneName,
                    ["levelName"] = levelName,
                    ["levelPath"] = levelPath,
                    ["internalLevelName"] = GCS.internalLevelName,
                    ["customLevelPaths"] = ConvertValue(GCS.customLevelPaths),
                    ["customLevelIndex"] = GCS.customLevelIndex,
                    ["isInternalLevel"] = ADOBase.isInternalLevel,
                    ["isCLSLevel"] = ADOBase.isCLSLevel,
                    ["isOfficialLevel"] = ADOBase.isOfficialLevel,
                    ["isPlayingLevel"] = ADOBase.isPlayingLevel,
                    ["speedTrialMode"] = GCS.speedTrialMode,
                    ["currentSpeedTrial"] = GCS.currentSpeedTrial,
                    ["practiceMode"] = GCS.practiceMode,
                    ["checkpointNum"] = GCS.checkpointNum,
                    ["worldEntrance"] = GCS.worldEntrance
                },
                ["world"] = worldData,
                ["controller"] = controller == null ? null : new Dictionary<string, object>
                {
                    ["currentWorldString"] = scrController.currentWorldString,
                    ["isBossLevel"] = controller.isbosslevel,
                    ["isPuzzleRoom"] = controller.isPuzzleRoom,
                    ["gameworld"] = controller.gameworld,
                    ["responsive"] = controller.responsive
                },
                ["levelData"] = BuildLevelData(scene),
                ["runtimeFloors"] = BuildFloors(floors)
            };
        }

        private static object BuildLevelData(scnGame scene)
        {
            var levelData = scene.levelData;
            return new Dictionary<string, object>
            {
                ["hash"] = levelData.Hash,
                ["version"] = levelData.version,
                ["isOldLevel"] = levelData.isOldLevel,
                ["legacyFlash"] = levelData.legacyFlash,
                ["legacyCamRelativeTo"] = levelData.legacyCamRelativeTo,
                ["legacyTween"] = levelData.legacyTween,
                ["disableV15Features"] = levelData.disableV15Features,
                ["pathData"] = levelData.pathData,
                ["angleDataCount"] = levelData.angleData != null ? levelData.angleData.Count : 0,
                ["angleData"] = ConvertValue(levelData.angleData),
                ["meta"] = new Dictionary<string, object>
                {
                    ["artist"] = levelData.artist,
                    ["song"] = levelData.song,
                    ["author"] = levelData.author,
                    ["songFilename"] = levelData.songFilename,
                    ["bpm"] = levelData.bpm,
                    ["volume"] = levelData.volume,
                    ["pitch"] = levelData.pitch,
                    ["offset"] = levelData.offset,
                    ["countdownTicks"] = levelData.countdownTicks,
                    ["previewImage"] = levelData.previewImage,
                    ["previewIcon"] = levelData.previewIcon,
                    ["difficulty"] = levelData.difficulty,
                    ["levelDesc"] = levelData.levelDesc,
                    ["levelTags"] = levelData.levelTags
                },
                ["settings"] = new Dictionary<string, object>
                {
                    ["songSettings"] = ConvertValue(levelData.songSettings != null ? levelData.songSettings.data : null),
                    ["levelSettings"] = ConvertValue(levelData.levelSettings != null ? levelData.levelSettings.data : null),
                    ["trackSettings"] = ConvertValue(levelData.trackSettings != null ? levelData.trackSettings.data : null),
                    ["backgroundSettings"] = ConvertValue(levelData.backgroundSettings != null ? levelData.backgroundSettings.data : null),
                    ["cameraSettings"] = ConvertValue(levelData.cameraSettings != null ? levelData.cameraSettings.data : null),
                    ["miscSettings"] = ConvertValue(levelData.miscSettings != null ? levelData.miscSettings.data : null),
                    ["eventSettings"] = ConvertValue(levelData.eventSettings != null ? levelData.eventSettings.data : null),
                    ["decorationSettings"] = ConvertValue(levelData.decorationSettings != null ? levelData.decorationSettings.data : null)
                },
                ["actions"] = BuildEvents(levelData.levelEvents),
                ["decorations"] = BuildEvents(levelData.decorations)
            };
        }

        private static List<object> BuildEvents(IEnumerable events)
        {
            List<object> list = new List<object>();
            if (events == null)
            {
                return list;
            }

            foreach (object item in events)
            {
                var ev = item as ADOFAI.LevelEvent;
                if (ev == null)
                {
                    continue;
                }

                list.Add(new Dictionary<string, object>
                {
                    ["floor"] = ev.floor,
                    ["eventType"] = ev.eventType.ToString(),
                    ["active"] = ev.active,
                    ["visible"] = ev.visible,
                    ["locked"] = ev.locked,
                    ["isDecoration"] = ev.IsDecoration,
                    ["data"] = ConvertValue(ev.data),
                    ["disabled"] = ConvertValue(ev.disabled)
                });
            }

            return list;
        }

        private static List<object> BuildFloors(List<scrFloor> floors)
        {
            List<object> list = new List<object>();
            if (floors == null)
            {
                return list;
            }

            for (int i = 0; i < floors.Count; i++)
            {
                scrFloor f = floors[i];
                if (f == null)
                {
                    continue;
                }

                list.Add(new Dictionary<string, object>
                {
                    ["seqID"] = f.seqID,
                    ["entryAngle"] = f.entryangle,
                    ["exitAngle"] = f.exitangle,
                    ["angleLength"] = f.angleLength,
                    ["entryTime"] = f.entryTime,
                    ["entryTimePitchAdj"] = f.entryTimePitchAdj,
                    ["entryBeat"] = f.entryBeat,
                    ["speed"] = f.speed,
                    ["isCCW"] = f.isCCW,
                    ["midSpin"] = f.midSpin,
                    ["numPlanets"] = f.numPlanets,
                    ["tapsNeeded"] = f.tapsNeeded,
                    ["holdLength"] = f.holdLength,
                    ["isSafe"] = f.isSafe,
                    ["auto"] = f.auto,
                    ["hideJudgment"] = f.hideJudgment,
                    ["countdownTicks"] = f.countdownTicks,
                    ["isPortal"] = f.isportal,
                    ["portalType"] = f.levelnumber.ToString(),
                    ["portalArguments"] = f.arguments,
                    ["freeroam"] = f.freeroam,
                    ["freeroamGenerated"] = f.freeroamGenerated,
                    ["freeroamDimensions"] = ConvertValue(f.freeroamDimensions),
                    ["nextSeqID"] = f.nextfloor != null ? f.nextfloor.seqID : -1,
                    ["prevSeqID"] = f.prevfloor != null ? f.prevfloor.seqID : -1
                });
            }

            return list;
        }

        private static object ConvertValue(object value)
        {
            if (value == null)
            {
                return null;
            }

            switch (value)
            {
                case string _:
                case bool _:
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                case decimal _:
                    return value;
                case Enum e:
                    return e.ToString();
                case Vector2 v2:
                    return new Dictionary<string, object> { ["x"] = v2.x, ["y"] = v2.y };
                case Vector3 v3:
                    return new Dictionary<string, object> { ["x"] = v3.x, ["y"] = v3.y, ["z"] = v3.z };
                case Color c:
                    return new Dictionary<string, object> { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };
                case IDictionary dictionary:
                {
                    Dictionary<string, object> mapped = new Dictionary<string, object>();
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        string key = entry.Key != null ? entry.Key.ToString() : "null";
                        mapped[key] = ConvertValue(entry.Value);
                    }
                    return mapped;
                }
                case IEnumerable enumerable:
                {
                    List<object> mapped = new List<object>();
                    foreach (object item in enumerable)
                    {
                        mapped.Add(ConvertValue(item));
                    }
                    return mapped;
                }
                case UnityEngine.Object unityObject:
                    return new Dictionary<string, object>
                    {
                        ["unityType"] = unityObject.GetType().Name,
                        ["name"] = unityObject.name
                    };
                default:
                    return value.ToString();
            }
        }

        private static string BestOf(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static string SanitizeFilename(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "level";
            }

            string output = value.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                output = output.Replace(invalid[i], '_');
            }

            return output;
        }

        private static string ToJson(object value)
        {
            StringBuilder sb = new StringBuilder(32768);
            WriteJsonValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteJsonValue(StringBuilder sb, object value, int depth)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case string s:
                    WriteJsonString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                case decimal _:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case IDictionary dictionary:
                    WriteJsonObject(sb, dictionary, depth);
                    return;
                case IEnumerable enumerable:
                    WriteJsonArray(sb, enumerable, depth);
                    return;
                default:
                    WriteJsonString(sb, value.ToString());
                    return;
            }
        }

        private static void WriteJsonObject(StringBuilder sb, IDictionary dictionary, int depth)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                sb.Append('\n');
                AppendIndent(sb, depth + 1);
                WriteJsonString(sb, entry.Key != null ? entry.Key.ToString() : "null");
                sb.Append(": ");
                WriteJsonValue(sb, entry.Value, depth + 1);
                first = false;
            }

            if (!first)
            {
                sb.Append('\n');
                AppendIndent(sb, depth);
            }

            sb.Append('}');
        }

        private static void WriteJsonArray(StringBuilder sb, IEnumerable array, int depth)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in array)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                sb.Append('\n');
                AppendIndent(sb, depth + 1);
                WriteJsonValue(sb, item, depth + 1);
                first = false;
            }

            if (!first)
            {
                sb.Append('\n');
                AppendIndent(sb, depth);
            }

            sb.Append(']');
        }

        private static void WriteJsonString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private static void AppendIndent(StringBuilder sb, int depth)
        {
            for (int i = 0; i < depth; i++)
            {
                sb.Append("  ");
            }
        }
    }
}
