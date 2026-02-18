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
        private const int MaxPoolSources = 32;

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
            public readonly Dictionary<string, AudioClip> TimeScaledClipCache = new Dictionary<string, AudioClip>();
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
            PlayListenStartNow(1f);
        }

        public static void PlayListenStartNow(float playbackRate)
        {
            PlayCueNow(ListenStartCueState, playbackRate, allowFallbackWhileCustomLoads: true);
        }

        public static void PlayListenStartAt(double dspTime)
        {
            PlayListenStartAt(dspTime, 1f);
        }

        public static void PlayListenStartAt(double dspTime, float playbackRate)
        {
            PlayCueAt(ListenStartCueState, dspTime, playbackRate, allowFallbackWhileCustomLoads: true);
        }

        public static void PlayListenEndNow()
        {
            PlayListenEndNow(1f);
        }

        public static void PlayListenEndNow(float playbackRate)
        {
            PlayCueNow(ListenEndCueState, playbackRate, allowFallbackWhileCustomLoads: true);
        }

        public static void PlayListenEndAt(double dspTime)
        {
            PlayListenEndAt(dspTime, 1f);
        }

        public static void PlayListenEndAt(double dspTime, float playbackRate)
        {
            PlayCueAt(ListenEndCueState, dspTime, playbackRate, allowFallbackWhileCustomLoads: true);
        }

        public static double GetListenStartCueDurationSeconds()
        {
            return GetCueDurationSeconds(ListenStartCueState, allowFallbackWhileCustomLoads: true);
        }

        public static double GetListenEndCueDurationSeconds()
        {
            return GetCueDurationSeconds(ListenEndCueState, allowFallbackWhileCustomLoads: true);
        }

        private static void PlayCueNow(CueClipState cueState, float playbackRate = 1f, bool allowFallbackWhileCustomLoads = false)
        {
            EnsureAudioReady(cueState);
            if (_cueSource == null)
            {
                return;
            }

            AudioClip clip = SelectClip(cueState, allowFallbackWhileCustomLoads);
            clip = GetPlaybackClip(cueState, clip, playbackRate);
            if (clip != null)
            {
                _cueSource.pitch = 1f;
                _cueSource.PlayOneShot(clip, 1f);
            }
        }

        private static void PlayCueAt(CueClipState cueState, double dspTime, float playbackRate = 1f, bool allowFallbackWhileCustomLoads = false)
        {
            EnsureAudioReady(cueState);

            AudioClip clip = SelectClip(cueState, allowFallbackWhileCustomLoads);
            clip = GetPlaybackClip(cueState, clip, playbackRate);
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
            double busyUntil = startDsp + Math.Max(clipLengthSeconds, 0.05f);

            for (int i = 0; i < CuePool.Count; i++)
            {
                CueSourceSlot existing = CuePool[i];
                if (existing.BusyUntilDsp <= nowDsp + 0.0001)
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

        private static AudioClip GetPlaybackClip(CueClipState cueState, AudioClip baseClip, float playbackRate)
        {
            if (baseClip == null)
            {
                return null;
            }

            float normalizedRate = NormalizePlaybackRate(playbackRate);
            if (Mathf.Abs(normalizedRate - 1f) < 0.01f)
            {
                return baseClip;
            }

            string key = baseClip.GetInstanceID().ToString() + "|" + normalizedRate.ToString("0.00");
            if (cueState.TimeScaledClipCache.TryGetValue(key, out AudioClip cached) && cached != null)
            {
                return cached;
            }

            AudioClip scaled = BuildTimeScaledClip(baseClip, normalizedRate, cueState.GeneratedClipName + "_TempoScaled_" + normalizedRate.ToString("0.00"));
            if (scaled == null)
            {
                return baseClip;
            }

            if (cueState.TimeScaledClipCache.Count >= 64)
            {
                cueState.TimeScaledClipCache.Clear();
            }

            cueState.TimeScaledClipCache[key] = scaled;
            return scaled;
        }

        private static AudioClip BuildTimeScaledClip(AudioClip source, float speedRate, string clipName)
        {
            try
            {
                if (source == null || source.channels <= 0 || source.frequency <= 0 || source.samples <= 0)
                {
                    return null;
                }

                int channels = source.channels;
                int inputFrames = source.samples;
                int totalInputSamples = inputFrames * channels;
                float[] input = new float[totalInputSamples];
                source.GetData(input, 0);

                if (inputFrames < 256)
                {
                    return source;
                }

                int windowSize = Mathf.Min(1024, inputFrames);
                int analysisHop = Mathf.Max(64, windowSize / 4);
                float timeScale = 1f / speedRate; // >1 slower, <1 faster
                int synthesisHop = Mathf.RoundToInt(analysisHop * timeScale);
                synthesisHop = Mathf.Clamp(synthesisHop, 32, windowSize - 32);
                int overlapSize = windowSize - synthesisHop;
                int searchRadius = Mathf.Max(16, analysisHop / 2);

                int frameCount = 1 + Mathf.Max(0, (inputFrames - windowSize) / analysisHop);
                int outputFrames = Mathf.Max(windowSize, windowSize + (frameCount - 1) * synthesisHop);
                int totalOutputSamples = outputFrames * channels;
                float[] output = new float[totalOutputSamples];
                float[] weights = new float[outputFrames];
                float[] window = BuildHannWindow(windowSize);

                // First frame anchors the synthesis timeline.
                AddWindowedFrame(input, inputFrames, channels, 0, output, outputFrames, 0, window, weights);
                int previousInStart = 0;

                for (int frame = 1; frame < frameCount; frame++)
                {
                    int outStart = frame * synthesisHop;
                    int targetInStart = previousInStart + analysisHop;
                    targetInStart = Mathf.Clamp(targetInStart, 0, inputFrames - windowSize);
                    int inStart = FindBestAlignment(input, output, inputFrames, outputFrames, channels, targetInStart, outStart, overlapSize, searchRadius);
                    AddWindowedFrame(input, inputFrames, channels, inStart, output, outputFrames, outStart, window, weights);
                    previousInStart = inStart;
                }

                for (int frame = 0; frame < outputFrames; frame++)
                {
                    float weight = weights[frame];
                    if (weight <= 0.0001f)
                    {
                        continue;
                    }

                    int frameIndex = frame * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        output[frameIndex + c] /= weight;
                    }
                }

                AudioClip result = AudioClip.Create(clipName, outputFrames, channels, source.frequency, false);
                result.SetData(output, 0);
                return result;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ADOFAI Access] Failed to build tempo-scaled cue clip: {ex}");
                return null;
            }
        }

        private static int FindBestAlignment(
            float[] input,
            float[] output,
            int inputFrames,
            int outputFrames,
            int channels,
            int targetInStart,
            int outStart,
            int overlapSize,
            int searchRadius)
        {
            if (overlapSize <= 0)
            {
                return Mathf.Clamp(targetInStart, 0, inputFrames - 1);
            }

            int minStart = Mathf.Max(0, targetInStart - searchRadius);
            int maxStart = Mathf.Min(inputFrames - overlapSize - 1, targetInStart + searchRadius);
            int outOverlapStart = outStart;
            if (outOverlapStart < 0 || outOverlapStart + overlapSize >= outputFrames)
            {
                return Mathf.Clamp(targetInStart, minStart, maxStart);
            }

            float bestScore = float.NegativeInfinity;
            int bestStart = Mathf.Clamp(targetInStart, minStart, maxStart);
            for (int candidate = minStart; candidate <= maxStart; candidate++)
            {
                float dot = 0f;
                float energyIn = 0f;
                float energyOut = 0f;

                for (int n = 0; n < overlapSize; n++)
                {
                    int inFrame = candidate + n;
                    int outFrame = outOverlapStart + n;
                    int inIndex = inFrame * channels;
                    int outIndex = outFrame * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        float a = input[inIndex + c];
                        float b = output[outIndex + c];
                        dot += a * b;
                        energyIn += a * a;
                        energyOut += b * b;
                    }
                }

                if (energyIn <= 0.000001f || energyOut <= 0.000001f)
                {
                    continue;
                }

                float score = dot / Mathf.Sqrt(energyIn * energyOut);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestStart = candidate;
                }
            }

            return bestStart;
        }

        private static void AddWindowedFrame(
            float[] input,
            int inputFrames,
            int channels,
            int inStart,
            float[] output,
            int outputFrames,
            int outStart,
            float[] window,
            float[] weights)
        {
            int windowSize = window.Length;
            for (int n = 0; n < windowSize; n++)
            {
                int inFrame = inStart + n;
                int outFrame = outStart + n;
                if (inFrame >= inputFrames || outFrame >= outputFrames || outFrame < 0)
                {
                    break;
                }

                float w = window[n];
                weights[outFrame] += w;
                int inIndex = inFrame * channels;
                int outIndex = outFrame * channels;
                for (int c = 0; c < channels; c++)
                {
                    output[outIndex + c] += input[inIndex + c] * w;
                }
            }
        }

        private static float[] BuildHannWindow(int size)
        {
            float[] window = new float[size];
            if (size <= 1)
            {
                if (size == 1)
                {
                    window[0] = 1f;
                }
                return window;
            }

            for (int i = 0; i < size; i++)
            {
                window[i] = 0.5f * (1f - Mathf.Cos((2f * Mathf.PI * i) / (size - 1)));
            }

            return window;
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

        private static float NormalizePlaybackRate(float playbackRate)
        {
            if (playbackRate <= 0f || float.IsNaN(playbackRate) || float.IsInfinity(playbackRate))
            {
                return 1f;
            }

            if (playbackRate < 0.5f)
            {
                return 0.5f;
            }

            if (playbackRate > 2f)
            {
                return 2f;
            }

            return playbackRate;
        }
    }
}
