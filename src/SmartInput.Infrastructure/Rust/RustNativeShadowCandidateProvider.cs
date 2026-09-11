using System.Runtime.InteropServices;
using System.Text;
using SmartInput.Core.Integration;

namespace SmartInput.Infrastructure.Rust;

/// <summary>
/// Loads the optional Rust scorer only when an explicit local DLL path is
/// configured. The adapter has no dependency on Windows input injection and
/// exposes no apply, Undo or learning operation; its bounded candidate may be
/// consumed by the C# hybrid gate in explicit allow-list mode.
/// </summary>
public sealed class RustNativeShadowCandidateProvider : IRustShadowCandidateProvider, IDisposable
{
    public const string LibraryPathEnvironmentVariable = "SMARTINPUT_RUST_ENGINE_DLL";
    private const uint RequiredAbiVersion = 1;
    private const uint LayoutCapability = 1;

    private readonly object _sync = new();
    private nint _library;
    private nint _engine;
    private EngineFreeDelegate? _engineFree;
    private SuggestDelegate? _suggest;
    private ResultFreeDelegate? _resultFree;
    private bool _disposed;

    private RustNativeShadowCandidateProvider(
        nint library,
        nint engine,
        EngineFreeDelegate engineFree,
        SuggestDelegate suggest,
        ResultFreeDelegate resultFree)
    {
        _library = library;
        _engine = engine;
        _engineFree = engineFree;
        _suggest = suggest;
        _resultFree = resultFree;
    }

    public RustShadowProviderState State => _disposed
        ? RustShadowProviderState.Disabled
        : RustShadowProviderState.Available;

    /// <summary>
    /// Resolves an optional absolute DLL path. A missing, malformed or
    /// incompatible provider fails closed and never prevents host startup.
    /// </summary>
    public static IRustShadowCandidateProvider CreateFromEnvironment()
    {
        var configuredPath = Environment.GetEnvironmentVariable(LibraryPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return new NullRustShadowCandidateProvider();
        }

        try
        {
            var libraryPath = Path.GetFullPath(configuredPath);
            if (!Path.IsPathFullyQualified(libraryPath) || !File.Exists(libraryPath))
            {
                return new NullRustShadowCandidateProvider(RustShadowProviderState.LibraryNotFound);
            }

            var library = NativeLibrary.Load(libraryPath);
            try
            {
                var abiVersion = GetDelegate<AbiVersionDelegate>(library, "smart_input_abi_version");
                var capabilities = GetDelegate<CapabilitiesDelegate>(library, "smart_input_capabilities");
                var engineNew = GetDelegate<EngineNewDelegate>(library, "smart_input_engine_new");
                var engineFree = GetDelegate<EngineFreeDelegate>(library, "smart_input_engine_free");
                var suggest = GetDelegate<SuggestDelegate>(library, "smart_input_suggest");
                var resultFree = GetDelegate<ResultFreeDelegate>(library, "smart_input_result_free");

                if (abiVersion() != RequiredAbiVersion
                    || capabilities().AbiVersion != RequiredAbiVersion
                    || (capabilities().FeatureFlags & LayoutCapability) == 0)
                {
                    NativeLibrary.Free(library);
                    return new NullRustShadowCandidateProvider(RustShadowProviderState.AbiMismatch);
                }

                var engine = engineNew();
                if (engine == 0)
                {
                    NativeLibrary.Free(library);
                    return new NullRustShadowCandidateProvider(RustShadowProviderState.InitializationFailed);
                }

                return new RustNativeShadowCandidateProvider(library, engine, engineFree, suggest, resultFree);
            }
            catch (EntryPointNotFoundException)
            {
                NativeLibrary.Free(library);
                return new NullRustShadowCandidateProvider(RustShadowProviderState.AbiMismatch);
            }
            catch (BadImageFormatException)
            {
                NativeLibrary.Free(library);
                return new NullRustShadowCandidateProvider(RustShadowProviderState.LibraryLoadFailed);
            }
        }
        catch (DllNotFoundException)
        {
            return new NullRustShadowCandidateProvider(RustShadowProviderState.LibraryNotFound);
        }
        catch (BadImageFormatException)
        {
            return new NullRustShadowCandidateProvider(RustShadowProviderState.LibraryLoadFailed);
        }
        catch (UnauthorizedAccessException)
        {
            return new NullRustShadowCandidateProvider(RustShadowProviderState.LibraryLoadFailed);
        }
        catch (IOException)
        {
            return new NullRustShadowCandidateProvider(RustShadowProviderState.LibraryLoadFailed);
        }
    }

    public RustShadowCandidateResult Evaluate(string token, string context)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(context);

        lock (_sync)
        {
            if (_disposed || _engine == 0 || _suggest is null || _resultFree is null)
            {
                return RustShadowCandidateResult.Unavailable(RustShadowProviderState.Disabled);
            }

            var tokenBytes = Encoding.UTF8.GetBytes(token);
            var contextBytes = Encoding.UTF8.GetBytes(context);
            var tokenPin = tokenBytes.Length == 0 ? default : GCHandle.Alloc(tokenBytes, GCHandleType.Pinned);
            var contextPin = contextBytes.Length == 0 ? default : GCHandle.Alloc(contextBytes, GCHandleType.Pinned);
            NativeResult result = default;

            try
            {
                result = _suggest(
                    _engine,
                    tokenBytes.Length == 0 ? 0 : tokenPin.AddrOfPinnedObject(),
                    (nuint)tokenBytes.Length,
                    contextBytes.Length == 0 ? 0 : contextPin.AddrOfPinnedObject(),
                    (nuint)contextBytes.Length);

                if (result.Status != (uint)NativeStatus.Ok)
                {
                    return RustShadowCandidateResult.Unavailable(RustShadowProviderState.NativeFailure);
                }

                var reason = MapReason(result.Reason);
                if (result.Action != 1 || result.ReplacementPointer == 0 || result.ReplacementLength == 0)
                {
                    return new RustShadowCandidateResult(
                        RustShadowProviderState.Available,
                        RustShadowDecision.Keep,
                        reason,
                        result.Confidence,
                        result.Margin);
                }

                // The native ABI caps tokens at 256 UTF-8 bytes. Recheck the
                // returned length before copying, because native input must
                // always be treated as untrusted at this boundary.
                if (result.ReplacementLength > 256)
                {
                    return RustShadowCandidateResult.Unavailable(RustShadowProviderState.NativeFailure);
                }

                var replacementBytes = new byte[(int)result.ReplacementLength];
                Marshal.Copy(result.ReplacementPointer, replacementBytes, 0, replacementBytes.Length);
                var replacement = new UTF8Encoding(false, true).GetString(replacementBytes);
                if (string.IsNullOrWhiteSpace(replacement))
                {
                    return RustShadowCandidateResult.Unavailable(RustShadowProviderState.NativeFailure);
                }

                return new RustShadowCandidateResult(
                    RustShadowProviderState.Available,
                    RustShadowDecision.Replace,
                    reason,
                    result.Confidence,
                    result.Margin,
                    replacement);
            }
            catch (ArgumentException)
            {
                return RustShadowCandidateResult.Unavailable(RustShadowProviderState.NativeFailure);
            }
            catch (SEHException)
            {
                return RustShadowCandidateResult.Unavailable(RustShadowProviderState.NativeFailure);
            }
            finally
            {
                // Required on every ABI result, including Keep/failure paths.
                _resultFree(ref result);
                if (tokenPin.IsAllocated)
                {
                    tokenPin.Free();
                }

                if (contextPin.IsAllocated)
                {
                    contextPin.Free();
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_engine != 0)
            {
                _engineFree?.Invoke(_engine);
                _engine = 0;
            }

            if (_library != 0)
            {
                NativeLibrary.Free(_library);
                _library = 0;
            }

            _engineFree = null;
            _suggest = null;
            _resultFree = null;
        }
    }

    private static TDelegate GetDelegate<TDelegate>(nint library, string export)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(NativeLibrary.GetExport(library, export));

    private static RustShadowReason MapReason(uint reason) => reason switch
    {
        1 => RustShadowReason.Layout,
        2 => RustShadowReason.Spelling,
        3 => RustShadowReason.ProtectedContext,
        4 => RustShadowReason.NoUsefulMapping,
        5 => RustShadowReason.LowConfidence,
        6 => RustShadowReason.ProtectedToken,
        7 => RustShadowReason.LearnedCorrection,
        _ => RustShadowReason.None,
    };

    private enum NativeStatus : uint
    {
        Ok = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCapabilities
    {
        public uint AbiVersion;
        public uint FeatureFlags;
        public nuint MaxTokenBytes;
        public nuint MaxContextBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeResult
    {
        public uint Status;
        public byte Action;
        private byte _reserved0;
        private byte _reserved1;
        private byte _reserved2;
        public uint Reason;
        public float Confidence;
        public float Margin;
        public nint ReplacementPointer;
        public nuint ReplacementLength;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeCapabilities CapabilitiesDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint EngineNewDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EngineFreeDelegate(nint engine);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeResult SuggestDelegate(
        nint engine,
        nint token,
        nuint tokenLength,
        nint context,
        nuint contextLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ResultFreeDelegate(ref NativeResult result);
}
