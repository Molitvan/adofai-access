using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.Networking;

namespace ADOFAI_Access
{
    internal static class TapCueService
    {
        private const int MaxPoolSources = 64;
        private const double CueSourceReleaseMarginSeconds = 0.02d;

        private sealed class CueClipState
        {
            public string FileName;
            public string EmbeddedResourceName;
            public string GeneratedClipName;
            public float GeneratedToneHz;
            public AudioClip CustomClip;
            public AudioClip FallbackClip;
            public bool EmbeddedCueLoadAttempted;
            public bool IsLoadingCue;
            public bool CustomCueLoadFailed;
            public string LoadedCuePath = string.Empty;
        }

        private sealed class CueSourceSlot
        {
            public AudioSource Source;
            public double BusyUntilDsp;
        }

        private static AudioSource _cueSource;
        private static readonly List<CueSourceSlot> CuePool = new List<CueSourceSlot>();
        private static readonly CueClipState TapCueState = new CueClipState
        {
            FileName = "tap.wav",
            EmbeddedResourceName = "ADOFAI_Access.Audio.tap.wav",
            GeneratedClipName = "ADOFAI_Access_DefaultPreviewCue",
            GeneratedToneHz = 1760f
        };
        private static readonly CueClipState ListenStartCueState = new CueClipState
        {
            FileName = "listen_start.wav",
            EmbeddedResourceName = "ADOFAI_Access.Audio.listen_start.wav",
            GeneratedClipName = "ADOFAI_Access_DefaultListenStartCue",
            GeneratedToneHz = 1318.51f
        };
        private static readonly CueClipState ListenEndCueState = new CueClipState
        {
            FileName = "listen_end.wav",
            EmbeddedResourceName = "ADOFAI_Access.Audio.listen_end.wav",
            GeneratedClipName = "ADOFAI_Access_DefaultListenEndCue",
            GeneratedToneHz = 987.77f
        };

        public static string CueFilePath
        {
            get { return GetCueFilePath(TapCueState.FileName); }
        }

        public static void PlayCueNow()
        {
            PlayCueNow(TapCueState);
        }

        public static void PlayCueAt(double dspTime)
        {
            PlayCueAt(TapCueState, dspTime);
        }

        public static void PlayListenStartNow()
        {
            PlayCueNow(ListenStartCueState, allowFallbackWhileCustomLoads: true);
        }

        public static void PlayListenStartAt(double dspTime)
        {
            PlayCueAt(ListenStartCueState, dspTime, allowFallbackWhileCustomLoads: true);
        }

        public static void PlayListenEndNow()
        {
            PlayCueNow(ListenEndCueState, allowFallbackWhileCustomLoads: true);
        }

        public static void PlayListenEndAt(double dspTime)
        {
            PlayCueAt(ListenEndCueState, dspTime, allowFallbackWhileCustomLoads: true);
        }

        public static double GetListenStartCueDurationSeconds()
        {
            return GetCueDurationSeconds(ListenStartCueState, allowFallbackWhileCustomLoads: true);
        }

        public static double GetListenEndCueDurationSeconds()
        {
            return GetCueDurationSeconds(ListenEndCueState, allowFallbackWhileCustomLoads: true);
        }

        private static void PlayCueNow(CueClipState cueState, bool allowFallbackWhileCustomLoads = false)
        {
            EnsureAudioReady(cueState);
            if (_cueSource == null)
            {
                return;
            }

            AudioClip clip = SelectClip(cueState, allowFallbackWhileCustomLoads);
            if (clip != null)
            {
                _cueSource.pitch = 1f;
                _cueSource.PlayOneShot(clip, 1f);
            }
        }

        private static void PlayCueAt(CueClipState cueState, double dspTime, bool allowFallbackWhileCustomLoads = false)
        {
            EnsureAudioReady(cueState);

            AudioClip clip = SelectClip(cueState, allowFallbackWhileCustomLoads);
            if (clip == null)
            {
                return;
            }

            CueSourceSlot slot = AcquirePoolSlot(dspTime, clip.length);
            if (slot == null || slot.Source == null)
            {
                return;
            }

            slot.Source.pitch = 1f;
            slot.Source.clip = clip;
            slot.Source.PlayScheduled(dspTime);
        }

        public static void StopAllCues()
        {
            if (_cueSource != null)
            {
                _cueSource.Stop();
            }

            for (int i = 0; i < CuePool.Count; i++)
            {
                CueSourceSlot slot = CuePool[i];
                if (slot?.Source == null)
                {
                    continue;
                }

                slot.Source.Stop();
                slot.BusyUntilDsp = 0d;
            }
        }

        private static CueSourceSlot AcquirePoolSlot(double dspTime, float clipLengthSeconds)
        {
            EnsureAudioSource();
            if (CuePool.Count == 0)
            {
                return null;
            }

            double nowDsp = AudioSettings.dspTime;
            double startDsp = Math.Max(dspTime, nowDsp);
            double busyUntil = startDsp + Math.Max(clipLengthSeconds, 0.05f) + CueSourceReleaseMarginSeconds;

            for (int i = 0; i < CuePool.Count; i++)
            {
                CueSourceSlot existing = CuePool[i];
                if (existing == null || existing.Source == null)
                {
                    continue;
                }

                if (existing.BusyUntilDsp <= nowDsp + 0.0001 && !existing.Source.isPlaying)
                {
                    existing.BusyUntilDsp = busyUntil;
                    return existing;
                }
            }

            if (CuePool.Count >= MaxPoolSources)
            {
                return null;
            }

            CueSourceSlot created = CreateCueSourceSlot();
            created.BusyUntilDsp = busyUntil;
            CuePool.Add(created);
            return created;
        }

        private static void EnsureAudioReady(CueClipState cueState)
        {
            EnsureAudioSource();
            EnsureFallbackClip(cueState);

            string cuePath = GetCueFilePath(cueState.FileName);
            if (cueState.CustomClip != null && string.Equals(cueState.LoadedCuePath, cuePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (cueState.IsLoadingCue)
            {
                return;
            }

            if (!File.Exists(cuePath))
            {
                cueState.CustomClip = null;
                cueState.LoadedCuePath = string.Empty;
                cueState.CustomCueLoadFailed = false;
                return;
            }

            cueState.IsLoadingCue = true;
            cueState.CustomCueLoadFailed = false;
            MelonCoroutines.Start(LoadCueClip(cuePath, cueState));
        }

        private static void EnsureAudioSource()
        {
            if (_cueSource != null)
            {
                return;
            }

            GameObject go = new GameObject("ADOFAI_Access_TapCueAudio");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _cueSource = go.AddComponent<AudioSource>();
            _cueSource.playOnAwake = false;
            _cueSource.spatialBlend = 0f;
            _cueSource.volume = 1f;

            CuePool.Add(CreateCueSourceSlot());
            CuePool.Add(CreateCueSourceSlot());
            CuePool.Add(CreateCueSourceSlot());
            CuePool.Add(CreateCueSourceSlot());
        }

        private static void EnsureFallbackClip(CueClipState cueState)
        {
            if (cueState.FallbackClip != null)
            {
                return;
            }

            if (!cueState.EmbeddedCueLoadAttempted)
            {
                cueState.EmbeddedCueLoadAttempted = true;
                cueState.FallbackClip = TryLoadEmbeddedCueClip(cueState);
                if (cueState.FallbackClip != null)
                {
                    return;
                }
            }

            const int sampleRate = 44100;
            const float durationSeconds = 0.045f;
            int sampleCount = Mathf.CeilToInt(sampleRate * durationSeconds);
            float[] samples = new float[sampleCount];
            float frequency = cueState.GeneratedToneHz > 0f ? cueState.GeneratedToneHz : 1760f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = 1f - i / (float)sampleCount;
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.25f;
            }

            string clipName = string.IsNullOrEmpty(cueState.GeneratedClipName) ? "ADOFAI_Access_DefaultCue" : cueState.GeneratedClipName;
            cueState.FallbackClip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            cueState.FallbackClip.SetData(samples, 0);
        }

        private static AudioClip TryLoadEmbeddedCueClip(CueClipState cueState)
        {
            try
            {
                Assembly assembly = typeof(TapCueService).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(cueState.EmbeddedResourceName))
                {
                    if (stream == null)
                    {
                        MelonLogger.Warning($"[ADOFAI Access] Embedded cue resource not found: {cueState.EmbeddedResourceName}");
                        return null;
                    }

                    AudioClip clip = CreateAudioClipFromWavStream(stream, cueState.GeneratedClipName + "_Embedded");
                    if (clip == null)
                    {
                        MelonLogger.Warning($"[ADOFAI Access] Embedded cue could not be decoded: {cueState.EmbeddedResourceName}");
                        return null;
                    }

                    return clip;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ADOFAI Access] Failed to load embedded cue {cueState.EmbeddedResourceName}: {ex}");
                return null;
            }
        }

        private static AudioClip CreateAudioClipFromWavStream(Stream stream, string clipName)
        {
            using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true))
            {
                if (stream.Length < 44)
                {
                    return null;
                }

                string riff = ReadFourCc(reader);
                if (riff != "RIFF")
                {
                    return null;
                }

                reader.ReadInt32(); // RIFF chunk size.
                string wave = ReadFourCc(reader);
                if (wave != "WAVE")
                {
                    return null;
                }

                bool hasFmt = false;
                bool hasData = false;
                ushort audioFormat = 1;
                ushort channels = 1;
                int sampleRate = 44100;
                ushort bitsPerSample = 16;
                byte[] dataBytes = null;

                while (stream.Position + 8 <= stream.Length)
                {
                    string chunkId = ReadFourCc(reader);
                    int chunkSize = reader.ReadInt32();
                    if (chunkSize < 0)
                    {
                        return null;
                    }

                    long chunkDataStart = stream.Position;
                    long nextChunkPos = chunkDataStart + chunkSize;
                    if (nextChunkPos > stream.Length)
                    {
                        return null;
                    }

                    if (chunkId == "fmt ")
                    {
                        if (chunkSize < 16)
                        {
                            return null;
                        }

                        audioFormat = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadInt32(); // byteRate
                        reader.ReadUInt16(); // blockAlign
                        bitsPerSample = reader.ReadUInt16();
                        hasFmt = true;
                    }
                    else if (chunkId == "data")
                    {
                        dataBytes = reader.ReadBytes(chunkSize);
                        hasData = true;
                    }

                    stream.Position = nextChunkPos;
                    if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
                    {
                        stream.Position++;
                    }

                    if (hasFmt && hasData)
                    {
                        break;
                    }
                }

                if (!hasFmt || !hasData || dataBytes == null || dataBytes.Length == 0 || channels == 0 || sampleRate <= 0)
                {
                    return null;
                }

                int bytesPerSample = bitsPerSample / 8;
                if (bytesPerSample <= 0)
                {
                    return null;
                }

                int totalSampleValues = dataBytes.Length / bytesPerSample;
                if (totalSampleValues <= 0)
                {
                    return null;
                }

                int frameCount = totalSampleValues / channels;
                if (frameCount <= 0)
                {
                    return null;
                }

                float[] samples = ConvertWavSamplesToFloat(dataBytes, audioFormat, bitsPerSample, totalSampleValues);
                if (samples == null || samples.Length == 0)
                {
                    return null;
                }

                AudioClip clip = AudioClip.Create(clipName, frameCount, channels, sampleRate, false);
                clip.SetData(samples, 0);
                return clip;
            }
        }

        private static float[] ConvertWavSamplesToFloat(byte[] dataBytes, ushort audioFormat, ushort bitsPerSample, int totalSampleValues)
        {
            float[] samples = new float[totalSampleValues];

            if (audioFormat == 1) // PCM
            {
                switch (bitsPerSample)
                {
                    case 8:
                        for (int i = 0; i < totalSampleValues; i++)
                        {
                            samples[i] = (dataBytes[i] - 128f) / 128f;
                        }
                        return samples;
                    case 16:
                        for (int i = 0; i < totalSampleValues; i++)
                        {
                            short sample = (short)(dataBytes[i * 2] | (dataBytes[i * 2 + 1] << 8));
                            samples[i] = sample / 32768f;
                        }
                        return samples;
                    case 24:
                        for (int i = 0; i < totalSampleValues; i++)
                        {
                            int index = i * 3;
                            int sample = dataBytes[index] | (dataBytes[index + 1] << 8) | (dataBytes[index + 2] << 16);
                            if ((sample & 0x800000) != 0)
                            {
                                sample |= unchecked((int)0xFF000000);
                            }
                            samples[i] = sample / 8388608f;
                        }
                        return samples;
                    case 32:
                        for (int i = 0; i < totalSampleValues; i++)
                        {
                            int index = i * 4;
                            int sample = dataBytes[index] | (dataBytes[index + 1] << 8) | (dataBytes[index + 2] << 16) | (dataBytes[index + 3] << 24);
                            samples[i] = sample / 2147483648f;
                        }
                        return samples;
                    default:
                        return null;
                }
            }

            if (audioFormat == 3 && bitsPerSample == 32) // IEEE float
            {
                for (int i = 0; i < totalSampleValues; i++)
                {
                    int index = i * 4;
                    samples[i] = BitConverter.ToSingle(dataBytes, index);
                }
                return samples;
            }

            return null;
        }

        private static string ReadFourCc(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length < 4)
            {
                return string.Empty;
            }
            return Encoding.ASCII.GetString(bytes, 0, 4);
        }

        private static IEnumerator LoadCueClip(string cuePath, CueClipState cueState)
        {
            string uri = new Uri(cuePath).AbsoluteUri;
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip != null)
                    {
                        cueState.CustomClip = clip;
                        cueState.LoadedCuePath = cuePath;
                        cueState.CustomCueLoadFailed = false;
                    }
                    else
                    {
                        cueState.CustomClip = null;
                        cueState.LoadedCuePath = string.Empty;
                        cueState.CustomCueLoadFailed = true;
                    }
                }
                else
                {
                    MelonLogger.Warning($"[ADOFAI Access] Failed to load tap cue from {cuePath}: {request.error}");
                    cueState.CustomClip = null;
                    cueState.LoadedCuePath = string.Empty;
                    cueState.CustomCueLoadFailed = true;
                }
            }

            cueState.IsLoadingCue = false;
        }

        private static AudioClip SelectClip(CueClipState cueState, bool allowFallbackWhileCustomLoads)
        {
            string cuePath = GetCueFilePath(cueState.FileName);
            if (cueState.CustomClip != null && string.Equals(cueState.LoadedCuePath, cuePath, StringComparison.OrdinalIgnoreCase))
            {
                return cueState.CustomClip;
            }

            if (File.Exists(cuePath) && !cueState.CustomCueLoadFailed && !allowFallbackWhileCustomLoads)
            {
                // Custom cue exists; wait until it is loaded instead of playing fallback first.
                return null;
            }

            return cueState.FallbackClip;
        }

        private static double GetCueDurationSeconds(CueClipState cueState, bool allowFallbackWhileCustomLoads)
        {
            EnsureAudioReady(cueState);
            AudioClip clip = SelectClip(cueState, allowFallbackWhileCustomLoads);
            if (clip == null)
            {
                return 0.0;
            }

            return Math.Max(0.0, clip.length);
        }

        private static CueSourceSlot CreateCueSourceSlot()
        {
            AudioSource pooled = _cueSource.gameObject.AddComponent<AudioSource>();
            pooled.playOnAwake = false;
            pooled.spatialBlend = 0f;
            pooled.volume = 1f;
            return new CueSourceSlot
            {
                Source = pooled,
                BusyUntilDsp = 0d
            };
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

        private static string GetCueFilePath(string fileName)
        {
            string gameRoot = GetGameRoot();
            return Path.Combine(gameRoot, "UserData", "ADOFAI_Access", "Audio", fileName);
        }

    }
}
