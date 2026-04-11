using System;
using System.Runtime.InteropServices;
using MelonLoader;

namespace ADOFAI_Access
{
    internal static class PrismSpeech
    {
        private const uint SupportedOutputFeatures =
            PrismBackendFeatureSupportsSpeak |
            PrismBackendFeatureSupportsBraille |
            PrismBackendFeatureSupportsOutput;

        private const uint PrismOk = 0;
        private const uint PrismErrorAlreadyInitialized = 15;
        private const uint PrismBackendFeatureSupportsSpeak = 1u << 2;
        private const uint PrismBackendFeatureSupportsBraille = 1u << 4;
        private const uint PrismBackendFeatureSupportsOutput = 1u << 5;
        private const uint PrismBackendFeatureSupportsIsSpeaking = 1u << 6;
        private const uint PrismBackendFeatureSupportsStop = 1u << 7;

        private static readonly object Sync = new object();

        private static IntPtr _context;
        private static IntPtr _backend;
        private static bool _loadAttempted;
        private static string _backendName = string.Empty;

        public static bool IsLoaded
        {
            get
            {
                lock (Sync)
                {
                    return _backend != IntPtr.Zero;
                }
            }
        }

        public static string BackendName
        {
            get
            {
                lock (Sync)
                {
                    return _backendName;
                }
            }
        }

        public static void Load()
        {
            lock (Sync)
            {
                if (_backend != IntPtr.Zero)
                {
                    return;
                }

                if (_loadAttempted)
                {
                    return;
                }

                _loadAttempted = true;
                try
                {
                    PrismConfig config = prism_config_init();
                    _context = prism_init(ref config);
                    if (_context == IntPtr.Zero)
                    {
                        MelonLogger.Warning("[ADOFAI Access] Prism initialization failed.");
                        return;
                    }

                    _backend = prism_registry_acquire_best(_context);
                    if (_backend == IntPtr.Zero)
                    {
                        MelonLogger.Warning("[ADOFAI Access] Prism found no available speech backend.");
                        ShutdownContext();
                        return;
                    }

                    uint initError = prism_backend_initialize(_backend);
                    if (initError != PrismOk && initError != PrismErrorAlreadyInitialized)
                    {
                        MelonLogger.Warning("[ADOFAI Access] Prism backend initialization failed: " + GetErrorString(initError));
                        ShutdownBackend();
                        ShutdownContext();
                        return;
                    }

                    ulong features = prism_backend_get_features(_backend);
                    if ((features & SupportedOutputFeatures) == 0)
                    {
                        MelonLogger.Warning("[ADOFAI Access] Prism backend does not support speech or output.");
                        ShutdownBackend();
                        ShutdownContext();
                        return;
                    }

                    _backendName = PtrToUtf8String(prism_backend_name(_backend)) ?? string.Empty;
                    string backendLabel = string.IsNullOrWhiteSpace(_backendName) ? "unknown" : _backendName;
                    MelonLogger.Msg("[ADOFAI Access] Prism speech initialized with backend: " + backendLabel);
                }
                catch (DllNotFoundException ex)
                {
                    MelonLogger.Warning("[ADOFAI Access] Prism native library not found (expected prism.dll): " + ex.Message);
                    ShutdownBackend();
                    ShutdownContext();
                }
                catch (EntryPointNotFoundException ex)
                {
                    MelonLogger.Warning("[ADOFAI Access] Prism native library is incompatible: " + ex.Message);
                    ShutdownBackend();
                    ShutdownContext();
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[ADOFAI Access] Prism initialization failed: " + ex);
                    ShutdownBackend();
                    ShutdownContext();
                }
            }
        }

        public static void Unload()
        {
            lock (Sync)
            {
                ShutdownBackend();
                ShutdownContext();
                _loadAttempted = false;
            }
        }

        public static bool Output(string text, bool interrupt = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            lock (Sync)
            {
                if (_backend == IntPtr.Zero)
                {
                    Load();
                }

                if (_backend == IntPtr.Zero)
                {
                    return false;
                }

                byte[] utf8 = Utf8NullTerminated(text);
                unsafe
                {
                    fixed (byte* pText = utf8)
                    {
                        uint error = prism_backend_output(_backend, (IntPtr)pText, interrupt);
                        if (error == PrismOk)
                        {
                            return true;
                        }

                        MelonLogger.Warning("[ADOFAI Access] Prism output failed: " + GetErrorString(error));
                        return false;
                    }
                }
            }
        }

        public static bool HasSpeech()
        {
            lock (Sync)
            {
                return _backend != IntPtr.Zero;
            }
        }

        public static bool HasBraille()
        {
            lock (Sync)
            {
                if (_backend == IntPtr.Zero)
                {
                    return false;
                }

                ulong features = prism_backend_get_features(_backend);
                return (features & PrismBackendFeatureSupportsBraille) != 0;
            }
        }

        public static bool IsSpeaking()
        {
            lock (Sync)
            {
                if (_backend == IntPtr.Zero)
                {
                    return false;
                }

                ulong features = prism_backend_get_features(_backend);
                if ((features & PrismBackendFeatureSupportsIsSpeaking) == 0)
                {
                    return false;
                }

                bool speaking;
                uint error = prism_backend_is_speaking(_backend, out speaking);
                return error == PrismOk && speaking;
            }
        }

        public static bool Silence()
        {
            lock (Sync)
            {
                if (_backend == IntPtr.Zero)
                {
                    return false;
                }

                ulong features = prism_backend_get_features(_backend);
                if ((features & PrismBackendFeatureSupportsStop) == 0)
                {
                    return false;
                }

                uint error = prism_backend_stop(_backend);
                return error == PrismOk;
            }
        }

        private static void ShutdownBackend()
        {
            if (_backend != IntPtr.Zero)
            {
                prism_backend_free(_backend);
                _backend = IntPtr.Zero;
            }

            _backendName = string.Empty;
        }

        private static void ShutdownContext()
        {
            if (_context != IntPtr.Zero)
            {
                prism_shutdown(_context);
                _context = IntPtr.Zero;
            }
        }

        private static string GetErrorString(uint error)
        {
            try
            {
                return PtrToUtf8String(prism_error_string(error)) ?? ("error " + error);
            }
            catch
            {
                return "error " + error;
            }
        }

        private static byte[] Utf8NullTerminated(string text)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            byte[] result = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            result[result.Length - 1] = 0;
            return result;
        }

        private static string PtrToUtf8String(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            int length = 0;
            while (Marshal.ReadByte(ptr, length) != 0)
            {
                length++;
            }

            byte[] buffer = new byte[length];
            Marshal.Copy(ptr, buffer, 0, length);
            return System.Text.Encoding.UTF8.GetString(buffer);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PrismConfig
        {
            public byte version;
        }

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern PrismConfig prism_config_init();

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_init(ref PrismConfig config);

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void prism_shutdown(IntPtr context);

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_registry_acquire_best(IntPtr context);

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void prism_backend_free(IntPtr backend);

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_backend_name(IntPtr backend);

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong prism_backend_get_features(IntPtr backend);

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint prism_backend_initialize(IntPtr backend);

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint prism_backend_output(IntPtr backend, IntPtr text, [MarshalAs(UnmanagedType.I1)] bool interrupt);

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint prism_backend_stop(IntPtr backend);

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint prism_backend_is_speaking(IntPtr backend, [MarshalAs(UnmanagedType.I1)] out bool speaking);

        [DllImport("prism.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_error_string(uint error);
    }
}
