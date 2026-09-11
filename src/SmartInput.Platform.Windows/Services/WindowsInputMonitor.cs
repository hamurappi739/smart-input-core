using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsInputMonitor : IInputMonitor, IDisposable
{
    private const string RuntimeMutexName = "Local\\SmartInput.KeyboardRuntime.v1";
    private const int ReplacementBarrierGraceMilliseconds = 250;
    private const int MaximumDeferredUserEvents = 256;

    private readonly ILogger<WindowsInputMonitor> _logger;
    private readonly IInputObservationFilter _observationFilter;
    private readonly IKeyboardCharacterResolver _characterResolver;
    private readonly ITextReplacementSessionNotifier _replacementSessionNotifier;
    private readonly IBoundaryKeyInterceptor _boundaryKeyInterceptor;
    private readonly IPredictionTabInterceptor _predictionTabInterceptor;
    private readonly IPredictionEscInterceptor _predictionEscInterceptor;
    private readonly IBoundaryKeyPairingTracker _boundaryKeyPairingTracker;
    private readonly IPerformanceMetricsRecorder _performanceMetrics;
    private readonly ConcurrentQueue<KeyboardObservationEventArgs> _pendingEvents = new();
    private readonly ConcurrentQueue<KeyboardObservationEventArgs> _deferredUserEvents = new();
    private readonly object _deferredReplaySync = new();
    private readonly HashSet<(int VirtualKeyCode, int ScanCode)> _deferredKeyDowns = [];
    private readonly object _lifecycleSync = new();
    private readonly Win32Keyboard.LowLevelKeyboardProc _hookProc;

    private Thread? _hookThread;
    private nint _hookHandle;
    private uint _hookThreadId;
    private volatile bool _isMonitoring;
    private volatile bool _disposed;
    // Counts suppressed boundaries that are queued or currently being
    // processed. While non-zero, releasing deferred user input would place
    // new text in front of the correction and recreate the duplicate-letter
    // race. This is independent from the short grace timer used by manual
    // replacement commands that have no intercepted boundary.
    private int _suppressedBoundaryCount;
    private long _replacementBarrierArmedAt;
    private ManualResetEventSlim? _threadReady;
    private Task? _dispatchTask;
    private CancellationTokenSource? _dispatchCts;
    private Mutex? _runtimeMutex;

    public WindowsInputMonitor(
        ILogger<WindowsInputMonitor> logger,
        IInputObservationFilter observationFilter,
        IKeyboardCharacterResolver characterResolver,
        ITextReplacementSessionNotifier replacementSessionNotifier,
        IBoundaryKeyInterceptor boundaryKeyInterceptor,
        IPredictionTabInterceptor predictionTabInterceptor,
        IPredictionEscInterceptor predictionEscInterceptor,
        IBoundaryKeyPairingTracker boundaryKeyPairingTracker,
        IPerformanceMetricsRecorder performanceMetrics)
    {
        _logger = logger;
        _observationFilter = observationFilter;
        _characterResolver = characterResolver;
        _replacementSessionNotifier = replacementSessionNotifier;
        _boundaryKeyInterceptor = boundaryKeyInterceptor;
        _predictionTabInterceptor = predictionTabInterceptor;
        _predictionEscInterceptor = predictionEscInterceptor;
        _boundaryKeyPairingTracker = boundaryKeyPairingTracker;
        _performanceMetrics = performanceMetrics;
        _hookProc = HookCallback;
    }

    public bool IsMonitoring => _isMonitoring;

    public event EventHandler<KeyboardObservationEventArgs>? InputObserved;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleSync)
        {
            if (_isMonitoring || _disposed)
            {
                return Task.CompletedTask;
            }

            // WH_KEYBOARD_LL is process-global from the user's perspective.
            // Two SmartInput processes would both observe the same physical
            // keystrokes and could each issue a replacement, producing doubled
            // letters. Keep exactly one owner per interactive Windows session.
            var runtimeMutex = new Mutex(initiallyOwned: false, RuntimeMutexName, out var createdNew);
            if (!createdNew)
            {
                runtimeMutex.Dispose();
                throw new InvalidOperationException(
                    "Another SmartInput keyboard runtime already owns the global hook.");
            }

            _runtimeMutex = runtimeMutex;

            try
            {
                _threadReady = new ManualResetEventSlim(false);
                _dispatchCts = new CancellationTokenSource();
                _hookThread = new Thread(HookThreadMain)
                {
                    IsBackground = true,
                    Name = "SmartInput-KeyboardHook",
                };
                _hookThread.SetApartmentState(ApartmentState.STA);
                _hookThread.Start();

                if (!_threadReady.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new InvalidOperationException("Keyboard hook thread failed to start.");
                }

                if (_hookHandle == 0)
                {
                    throw new InvalidOperationException("Failed to install low-level keyboard hook.");
                }

                _dispatchTask = Task.Run(() => DispatchLoopAsync(_dispatchCts.Token), _dispatchCts.Token);
                _isMonitoring = true;
                _logger.LogInformation("Low-level keyboard monitoring started.");
            }
            catch
            {
                _runtimeMutex.Dispose();
                _runtimeMutex = null;
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleSync)
        {
            if (!_isMonitoring)
            {
                return Task.CompletedTask;
            }

            _isMonitoring = false;
            _boundaryKeyPairingTracker.ClearPending(BoundaryPairingCleanupReason.HookShutdown);

            if (_hookThreadId != 0)
            {
                Win32Keyboard.PostThreadMessage(_hookThreadId, Win32Keyboard.WmQuit, 0, 0);
            }

            _dispatchCts?.Cancel();

            if (_hookThread is not null)
            {
                _hookThread.Join(TimeSpan.FromSeconds(3));
                _hookThread = null;
            }

            _dispatchTask?.Wait(TimeSpan.FromSeconds(2));
            _dispatchTask = null;

            _dispatchCts?.Dispose();
            _dispatchCts = null;

            _threadReady?.Dispose();
            _threadReady = null;

            DrainPendingEvents();

            _runtimeMutex?.Dispose();
            _runtimeMutex = null;

            _logger.LogInformation("Low-level keyboard monitoring stopped.");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
    }

    private void HookThreadMain()
    {
        _hookThreadId = GetCurrentThreadId();

        var moduleHandle = Win32Keyboard.GetModuleHandle(null);
        _hookHandle = Win32Keyboard.SetWindowsHookEx(
            Win32Keyboard.WhKeyboardLl,
            _hookProc,
            moduleHandle,
            0);

        _threadReady?.Set();

        if (_hookHandle == 0)
        {
            return;
        }

        while (Win32Keyboard.GetMessage(out var message, 0, 0, 0) > 0)
        {
            Win32Keyboard.TranslateMessage(ref message);
            Win32Keyboard.DispatchMessage(ref message);
        }

        if (_hookHandle != 0)
        {
            Win32Keyboard.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _isMonitoring)
        {
            var hookStartedAt = Stopwatch.GetTimestamp();
            try
            {
                return HandleHookEvent(nCode, wParam, lParam);
            }
            finally
            {
                _performanceMetrics.RecordDuration(
                    PerformanceMetricKind.HookCallback,
                    Stopwatch.GetElapsedTime(hookStartedAt).TotalMilliseconds);
            }
        }

        return Win32Keyboard.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private nint HandleHookEvent(int nCode, nint wParam, nint lParam)
    {
        var eventType = MapEventType(wParam);
        if (eventType is not null)
        {
            _performanceMetrics.RecordLivePipelineStage(LivePipelineStage.HookObserved);
            var hookStruct = Marshal.PtrToStructure<Win32Keyboard.KbdLlHookStruct>(lParam);
            var metadata = new KeyboardHookMetadata(hookStruct.Flags, hookStruct.ExtraInfo);
            var observation = new KeyboardObservationEventArgs
            {
                VirtualKeyCode = (int)hookStruct.VkCode,
                ScanCode = (int)(hookStruct.ScanCode & 0xFF),
                HookFlags = hookStruct.Flags,
                EventType = eventType.Value,
                TimestampUtc = DateTimeOffset.UtcNow,
                ForegroundWindowHandle = Win32Window.GetForegroundWindow(),
            };

            // Check KeyDown before resolver/boundary processing so deferred
            // user input cannot mutate the preflight token a second time.
            // Boundary KeyUp is intentionally checked by the pairing tracker
            // below before it can enter this queue.
            if (eventType == KeyEventType.KeyDown
                && ShouldDeferUserEvent(metadata)
                && TryDeferUserEventWithCapture(observation))
            {
                if (_boundaryKeyInterceptor.HasPendingEarlyLayoutCorrection())
                {
                    // Unlike a boundary correction, the current character has
                    // already been allowed through. Keep later characters in
                    // the bounded queue until the prefix replacement and
                    // layout request finish.
                    ArmReplacementBarrier();
                }

                return 1;
            }

            // Keep a key-up paired with a key-down that was already held by
            // the replacement barrier. Without this, a fast key can reach
            // the target as an orphan key-up before its replayed key-down.
            if (eventType == KeyEventType.KeyUp
                && !InputObservationRules.IsInjectedInput(
                    metadata,
                    SmartInputInjectionMarkers.SmartInputExtraInfo)
                && HasDeferredKeyDown(observation))
            {
                if (TryDeferUserEvent(observation))
                {
                    RemoveDeferredKeyDown(observation);
                    return 1;
                }

                RemoveDeferredKeyDown(observation);
            }

            var intercept = _boundaryKeyInterceptor.TryIntercept(observation, metadata);
            if (intercept.ShouldSuppress)
            {
                if (intercept.Boundary is not null)
                {
                    observation = new KeyboardObservationEventArgs
                    {
                        VirtualKeyCode = observation.VirtualKeyCode,
                        ScanCode = observation.ScanCode,
                        HookFlags = observation.HookFlags,
                        EventType = observation.EventType,
                        TimestampUtc = observation.TimestampUtc,
                        ForegroundWindowHandle = observation.ForegroundWindowHandle,
                        SuppressedBoundary = intercept.Boundary,
                        PreparedLayoutCorrection = intercept.PreparedCorrection,
                        CompletedToken = intercept.CompletedToken,
                        Resolved = intercept.Resolution,
                    };

                    _pendingEvents.Enqueue(observation);
                    Interlocked.Increment(ref _suppressedBoundaryCount);
                    _performanceMetrics.RecordInputQueueDepth(_pendingEvents.Count);
                    ArmReplacementBarrier();
                }

                return 1;
            }

            // The key that CREATED an early-layout proposal is part of the
            // prefix being replaced. It must reach both the target and the
            // serialized coordinator. Only keys arriving after this one are
            // future input and may be held behind the barrier.
            var shouldDeferAfterInterception = ShouldDeferAfterInterception(
                eventType.Value,
                intercept.IsEarlyLayoutCorrection,
                ShouldDeferUserEvent(metadata));
            if (intercept.IsEarlyLayoutCorrection)
            {
                ArmReplacementBarrier();
            }

            // A correction boundary is suppressed before the asynchronous
            // coordinator can start replacement. Hold the user's next real
            // keyboard events in a bounded in-memory queue so they cannot land
            // between the original token and its replacement. Injected
            // SmartInput events are intentionally excluded.
            if (shouldDeferAfterInterception
                && TryDeferUserEventWithCapture(observation))
            {
                return 1;
            }

            var tabIntercept = _predictionTabInterceptor.TryIntercept(observation, metadata);
            if (tabIntercept.ShouldSuppress)
            {
                if (tabIntercept.IsTabAcceptance && observation.EventType == KeyEventType.KeyDown)
                {
                    observation = new KeyboardObservationEventArgs
                    {
                        VirtualKeyCode = observation.VirtualKeyCode,
                        ScanCode = observation.ScanCode,
                        EventType = observation.EventType,
                        TimestampUtc = observation.TimestampUtc,
                        IsPredictionTabAcceptance = true,
                    };

                    _pendingEvents.Enqueue(observation);
                    _performanceMetrics.RecordInputQueueDepth(_pendingEvents.Count);
                }

                return 1;
            }

            var escIntercept = _predictionEscInterceptor.TryIntercept(observation, metadata);
            if (escIntercept.ShouldSuppress)
            {
                if (escIntercept.IsEscDismissal && observation.EventType == KeyEventType.KeyDown)
                {
                    observation = new KeyboardObservationEventArgs
                    {
                        VirtualKeyCode = observation.VirtualKeyCode,
                        ScanCode = observation.ScanCode,
                        EventType = observation.EventType,
                        TimestampUtc = observation.TimestampUtc,
                        IsPredictionEscDismissal = true,
                    };

                    _pendingEvents.Enqueue(observation);
                    _performanceMetrics.RecordInputQueueDepth(_pendingEvents.Count);
                }

                return 1;
            }

            if (_observationFilter.ShouldObserve(metadata))
            {
                if (intercept.CompletedToken is not null || intercept.Resolution is not null)
                {
                    observation = new KeyboardObservationEventArgs
                    {
                        VirtualKeyCode = observation.VirtualKeyCode,
                        ScanCode = observation.ScanCode,
                        HookFlags = observation.HookFlags,
                        EventType = observation.EventType,
                        TimestampUtc = observation.TimestampUtc,
                        ForegroundWindowHandle = observation.ForegroundWindowHandle,
                        CompletedToken = intercept.CompletedToken,
                        Resolved = intercept.Resolution,
                        PreparedLayoutCorrection = intercept.PreparedCorrection,
                        IsEarlyLayoutCorrection = intercept.IsEarlyLayoutCorrection,
                    };
                }

                if (_replacementSessionNotifier.IsReplacementActive)
                {
                    _replacementSessionNotifier.NotifyKeyboardEventDuringReplacement(
                        eventType.Value,
                        metadata);
                }

                _pendingEvents.Enqueue(observation);
                _performanceMetrics.RecordInputQueueDepth(_pendingEvents.Count);
            }
            else
            {
                _performanceMetrics.RecordLivePipelineStage(LivePipelineStage.HookFiltered);
                _performanceMetrics.RecordUncertainInputEvent();
            }
        }

        return Win32Keyboard.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_pendingEvents.TryDequeue(out var observation))
            {
                var latencyMs = (DateTimeOffset.UtcNow - observation.TimestampUtc).TotalMilliseconds;
                _performanceMetrics.RecordDuration(PerformanceMetricKind.InputDispatchLatency, latencyMs);
                _performanceMetrics.RecordInputQueueDepth(_pendingEvents.Count);

                try
                {
                    InputObserved?.Invoke(this, observation);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Input observation handler failed.");
                }
                finally
                {
                    ReleaseSuppressedBoundary(observation);
                }

                FlushDeferredUserEventsIfReady();

                continue;
            }

            try
            {
                FlushDeferredUserEventsIfReady();
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        DrainPendingEvents();
        FlushDeferredUserEventsIfReady(force: true);
    }

    private void DrainPendingEvents()
    {
        while (_pendingEvents.TryDequeue(out var observation))
        {
            try
            {
                InputObserved?.Invoke(this, observation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Input observation handler failed during drain.");
            }
            finally
            {
                ReleaseSuppressedBoundary(observation);
            }
        }
    }

    private void ReleaseSuppressedBoundary(KeyboardObservationEventArgs observation)
    {
        if (observation.SuppressedBoundary is not null)
        {
            if (Interlocked.Decrement(ref _suppressedBoundaryCount) <= 0)
            {
                // The physical boundary has now either been delivered or
                // failed open. The barrier is no longer needed; keeping its
                // grace timestamp would unnecessarily hold the next typed
                // character for another 250 ms.
                Interlocked.Exchange(ref _suppressedBoundaryCount, 0);
                Interlocked.Exchange(ref _replacementBarrierArmedAt, 0);
            }
        }
    }

    private void ArmReplacementBarrier()
    {
        Interlocked.Exchange(ref _replacementBarrierArmedAt, Stopwatch.GetTimestamp());
    }

    private bool ShouldDeferUserEvent(KeyboardHookMetadata metadata)
    {
        if (InputObservationRules.IsInjectedInput(
            metadata,
            SmartInputInjectionMarkers.SmartInputExtraInfo))
        {
            return false;
        }

        // Manual Double Shift and Undo start a replacement without having
        // intercepted a boundary first. Once the replacement session is live,
        // hold genuine events too; otherwise a fast next character can land
        // between Backspace and Unicode insertion.
        if (_replacementSessionNotifier.IsReplacementActive)
        {
            return true;
        }

        if (_boundaryKeyInterceptor.HasPendingEarlyLayoutCorrection())
        {
            return true;
        }

        // A boundary has been removed from the target application's input
        // stream and is still queued/in-flight. Do not let the grace timer
        // expire and replay later keystrokes before that boundary is resolved.
        if (Volatile.Read(ref _suppressedBoundaryCount) > 0)
        {
            return true;
        }

        var armedAt = Interlocked.Read(ref _replacementBarrierArmedAt);
        if (armedAt == 0)
        {
            return false;
        }

        if (!_replacementSessionNotifier.IsReplacementActive
            && Stopwatch.GetElapsedTime(armedAt).TotalMilliseconds > ReplacementBarrierGraceMilliseconds)
        {
            // Fail open after a bounded grace period if the correction never
            // started (for example because a policy changed). The dispatcher
            // will replay anything already buffered.
            Interlocked.CompareExchange(ref _replacementBarrierArmedAt, 0, armedAt);
            return false;
        }

        return true;
    }

    private bool TryDeferUserEvent(KeyboardObservationEventArgs observation)
    {
        if (_deferredUserEvents.Count >= MaximumDeferredUserEvents)
        {
            Interlocked.Exchange(ref _replacementBarrierArmedAt, 0);
            return false;
        }

        _deferredUserEvents.Enqueue(observation);
        return true;
    }

    private void FlushDeferredUserEventsIfReady(bool force = false)
    {
        var hasOutstandingSuppressedBoundary =
            Volatile.Read(ref _suppressedBoundaryCount) > 0;
        if (hasOutstandingSuppressedBoundary && !force)
        {
            return;
        }

        var armedAt = Interlocked.Read(ref _replacementBarrierArmedAt);
        var replacementActive = _replacementSessionNotifier.IsReplacementActive;
        var earlyCorrectionPending = _boundaryKeyInterceptor.HasPendingEarlyLayoutCorrection();

        // Once the early transaction has either completed or failed, there is
        // no reason to keep later physical keys for the old 250 ms fail-open
        // timeout. Releasing here removes the visible typing pause while the
        // pending/active checks still keep input behind an in-flight rewrite.
        if (armedAt != 0
            && CanReleaseCompletedEarlyBarrier(
                replacementActive,
                hasOutstandingSuppressedBoundary,
                earlyCorrectionPending)
            && Interlocked.CompareExchange(ref _replacementBarrierArmedAt, 0, armedAt) == armedAt)
        {
            armedAt = 0;
        }

        if (replacementActive
            || (armedAt == 0 && _deferredUserEvents.IsEmpty))
        {
            return;
        }

        if (armedAt != 0
            && !force
            && !hasOutstandingSuppressedBoundary
            && Stopwatch.GetElapsedTime(armedAt).TotalMilliseconds < ReplacementBarrierGraceMilliseconds)
        {
            return;
        }

        // Do not replay a key-down before its physical key-up has been held by
        // the same barrier. This preserves event pairing for fast typing and
        // prevents the target from observing a stale key state.
        if (!force && HasDeferredKeyDowns())
        {
            return;
        }

        if (armedAt != 0
            && Interlocked.CompareExchange(ref _replacementBarrierArmedAt, 0, armedAt) != armedAt)
        {
            return;
        }

        var replay = new List<KeyboardObservationEventArgs>();
        while (_deferredUserEvents.TryDequeue(out var deferred))
        {
            replay.Add(deferred);
        }

        if (replay.Count == 0)
        {
            return;
        }

        var inputs = BuildDeferredReplayInputs(replay);

        var sent = Win32Input.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<Win32Input.Input>());
        if (sent != inputs.Length)
        {
            _logger.LogWarning("Deferred user input replay was only partially accepted.");
            return;
        }

        ClearDeferredKeyDowns();

        // The replay is marked with SmartInputExtraInfo, so the low-level hook
        // intentionally filters it out as injected input. Notify the internal
        // observers once after the whole batch was accepted, otherwise their
        // token buffers would silently lose fast text typed during a
        // replacement. The live correction coordinator processes these events
        // synchronously and therefore cannot race the boundary that opened the
        // barrier.
        foreach (var deferred in replay)
        {
            try
            {
                InputObserved?.Invoke(this, new KeyboardObservationEventArgs
                {
                    VirtualKeyCode = deferred.VirtualKeyCode,
                    ScanCode = deferred.ScanCode,
                    HookFlags = deferred.HookFlags,
                    EventType = deferred.EventType,
                    TimestampUtc = deferred.TimestampUtc,
                    ForegroundWindowHandle = deferred.ForegroundWindowHandle,
                    ResolvedCharacter = deferred.ResolvedCharacter,
                    ResolvedBoundary = deferred.ResolvedBoundary,
                    IsDeferredReplay = true,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deferred user input reconciliation failed.");
            }
        }
    }

    internal static bool ShouldDeferAfterInterception(
        KeyEventType eventType,
        bool isEarlyLayoutCorrectionTrigger,
        bool barrierRequestsDeferral)
        => eventType == KeyEventType.KeyDown
            && barrierRequestsDeferral
            && !isEarlyLayoutCorrectionTrigger;

    internal static bool CanReleaseCompletedEarlyBarrier(
        bool replacementActive,
        bool hasOutstandingSuppressedBoundary,
        bool earlyCorrectionPending)
        => !replacementActive
            && !hasOutstandingSuppressedBoundary
            && !earlyCorrectionPending;

    private bool TryDeferUserEventWithCapture(KeyboardObservationEventArgs observation)
    {
        var captured = CaptureReplayResolution(observation);
        if (!TryDeferUserEvent(captured))
        {
            return false;
        }

        if (observation.EventType == KeyEventType.KeyDown)
        {
            lock (_deferredReplaySync)
            {
                _deferredKeyDowns.Add((observation.VirtualKeyCode, observation.ScanCode));
            }
        }

        return true;
    }

    private KeyboardObservationEventArgs CaptureReplayResolution(
        KeyboardObservationEventArgs observation)
    {
        if (observation.EventType != KeyEventType.KeyDown)
        {
            return observation;
        }

        // An early layout correction deliberately changes the active layout
        // before deferred keys are replayed. Capturing their character now
        // would freeze the old-layout interpretation and re-inject it after
        // the switch (for example, an English `t` instead of Russian `е`).
        // Replay the physical key and resolve it again after the switch.
        if (_boundaryKeyInterceptor.HasPendingEarlyLayoutCorrection())
        {
            return observation;
        }

        var resolution = _characterResolver.Resolve(observation);
        if (resolution.Kind == CharacterResolutionKind.Character)
        {
            return CloneWithReplayResolution(observation, resolution.Character, null);
        }

        if (resolution.Kind == CharacterResolutionKind.WordBoundary
            && resolution.Boundary is not null)
        {
            return CloneWithReplayResolution(observation, null, resolution.Boundary);
        }

        return observation;
    }

    private static KeyboardObservationEventArgs CloneWithReplayResolution(
        KeyboardObservationEventArgs observation,
        char? resolvedCharacter,
        DeferredBoundaryKey? resolvedBoundary)
    {
        return new KeyboardObservationEventArgs
        {
            VirtualKeyCode = observation.VirtualKeyCode,
            ScanCode = observation.ScanCode,
            HookFlags = observation.HookFlags,
            EventType = observation.EventType,
            TimestampUtc = observation.TimestampUtc,
            ForegroundWindowHandle = observation.ForegroundWindowHandle,
            SuppressedBoundary = observation.SuppressedBoundary,
            PreparedLayoutCorrection = observation.PreparedLayoutCorrection,
            CompletedToken = observation.CompletedToken,
            ResolvedCharacter = resolvedCharacter,
            ResolvedBoundary = resolvedBoundary,
            Resolved = resolvedCharacter is char character
                ? KeyboardCharacterResolution.CharacterOf(character)
                : resolvedBoundary is not null
                    ? KeyboardCharacterResolution.CreateBoundary(resolvedBoundary)
                    : null,
            IsPredictionTabAcceptance = observation.IsPredictionTabAcceptance,
            IsPredictionEscDismissal = observation.IsPredictionEscDismissal,
            IsDeferredReplay = observation.IsDeferredReplay,
        };
    }

    private bool HasDeferredKeyDown(KeyboardObservationEventArgs observation)
    {
        lock (_deferredReplaySync)
        {
            return _deferredKeyDowns.Contains((observation.VirtualKeyCode, observation.ScanCode));
        }
    }

    private bool HasDeferredKeyDowns()
    {
        lock (_deferredReplaySync)
        {
            return _deferredKeyDowns.Count > 0;
        }
    }

    private void RemoveDeferredKeyDown(KeyboardObservationEventArgs observation)
    {
        lock (_deferredReplaySync)
        {
            _deferredKeyDowns.Remove((observation.VirtualKeyCode, observation.ScanCode));
        }
    }

    private void ClearDeferredKeyDowns()
    {
        lock (_deferredReplaySync)
        {
            _deferredKeyDowns.Clear();
        }
    }

    private static Win32Input.Input CreateReplayKeyboardInput(
        ushort virtualKey,
        ushort scanCode,
        uint flags)
    {
        return new Win32Input.Input
        {
            Type = Win32Input.InputKeyboard,
            Data = new Win32Input.InputUnion
            {
                Keyboard = new Win32Input.KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                    ExtraInfo = SmartInputInjectionMarkers.SmartInputExtraInfo,
                },
            },
        };
    }

    internal static Win32Input.Input[] BuildDeferredReplayInputs(
        IReadOnlyList<KeyboardObservationEventArgs> replay)
    {
        ArgumentNullException.ThrowIfNull(replay);

        var inputs = new Win32Input.Input[replay.Count];
        var charactersByKey = new Dictionary<(int VirtualKeyCode, int ScanCode), char>();
        for (var index = 0; index < replay.Count; index++)
        {
            var deferred = replay[index];
            var key = (deferred.VirtualKeyCode, deferred.ScanCode);

            if (deferred.EventType == KeyEventType.KeyDown
                && TryGetResolvedReplayCharacter(deferred, out var character))
            {
                charactersByKey[key] = character;
                inputs[index] = CreateReplayUnicodeInput(character, keyUp: false);
                continue;
            }

            if (deferred.EventType == KeyEventType.KeyUp
                && charactersByKey.Remove(key, out character))
            {
                inputs[index] = CreateReplayUnicodeInput(character, keyUp: true);
                continue;
            }

            var flags = (deferred.HookFlags & Win32Keyboard.LlkhfExtended) != 0
                ? Win32Input.KeyeventfExtendedKey
                : 0u;
            if (deferred.EventType == KeyEventType.KeyUp)
            {
                flags |= Win32Input.KeyeventfKeyUp;
            }

            inputs[index] = CreateReplayKeyboardInput(
                (ushort)deferred.VirtualKeyCode,
                (ushort)deferred.ScanCode,
                flags);
        }

        return inputs;
    }

    private static bool TryGetResolvedReplayCharacter(
        KeyboardObservationEventArgs observation,
        out char character)
    {
        if (observation.ResolvedCharacter is char resolvedCharacter)
        {
            character = resolvedCharacter;
            return true;
        }

        if (observation.Resolved?.Kind == CharacterResolutionKind.Character)
        {
            character = observation.Resolved.Character;
            return true;
        }

        if (observation.ResolvedBoundary?.DeliveryKind == BoundaryDeliveryKind.UnicodeCharacter
            && observation.ResolvedBoundary.Character is char boundaryCharacter)
        {
            character = boundaryCharacter;
            return true;
        }

        character = default;
        return false;
    }

    private static Win32Input.Input CreateReplayUnicodeInput(char character, bool keyUp)
    {
        return new Win32Input.Input
        {
            Type = Win32Input.InputKeyboard,
            Data = new Win32Input.InputUnion
            {
                Keyboard = new Win32Input.KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = character,
                    Flags = Win32Input.KeyeventfUnicode
                        | (keyUp ? Win32Input.KeyeventfKeyUp : 0u),
                    ExtraInfo = SmartInputInjectionMarkers.SmartInputExtraInfo,
                },
            },
        };
    }

    private static KeyEventType? MapEventType(nint wParam)
    {
        return wParam.ToInt32() switch
        {
            Win32Keyboard.WmKeyDown or Win32Keyboard.WmSysKeyDown => KeyEventType.KeyDown,
            Win32Keyboard.WmKeyUp or Win32Keyboard.WmSysKeyUp => KeyEventType.KeyUp,
            _ => null,
        };
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
