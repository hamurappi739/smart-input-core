using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Input;
using PlatformKeyEventType = SmartInput.Platform.Abstractions.Input.KeyEventType;

namespace SmartInput.App.Services;

public interface IDoubleShiftUndoCoordinator
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Recognizes two released Shift taps without suppressing either key. This
/// deliberately leaves normal capitalization and selection behavior intact.
/// </summary>
public sealed class DoubleShiftUndoCoordinator : IDoubleShiftUndoCoordinator, IAsyncDisposable
{
    private const int DefaultDoubleShiftWindowMilliseconds = 350;
    private const int MinimumDoubleShiftWindowMilliseconds = 150;
    private const int MaximumDoubleShiftWindowMilliseconds = 1_000;

    private readonly IInputMonitor _inputMonitor;
    private readonly ICorrectionUndoService _correctionUndoService;
    private readonly ISafeTextReplacementService? _safeTextReplacementService;
    private readonly RecentTextContextBuffer? _recentTextContext;
    private readonly ILayoutConversionService? _layoutConversionService;
    private readonly ISettingsService? _settingsService;
    private readonly IKeyboardModifierState? _keyboardModifierState;
    private readonly IPerformanceMetricsRecorder _performanceMetrics;
    private readonly ILogger<DoubleShiftUndoCoordinator> _logger;
    private readonly SerializedObservationQueue _observationQueue;
    private readonly object _sync = new();

    private DateTimeOffset? _firstShiftReleasedAt;
    private bool _shiftIsDown;
    private bool _secondTapInProgress;
    private bool _controlIsDown;
    private bool _altIsDown;
    private bool _winIsDown;
    private bool _initialized;
    private int _undoInvocationGate;
    private CancellationTokenSource? _pendingActionCancellation;

    public DoubleShiftUndoCoordinator(
        IInputMonitor inputMonitor,
        ICorrectionUndoService correctionUndoService,
        IPerformanceMetricsRecorder performanceMetrics,
        ILogger<DoubleShiftUndoCoordinator> logger,
        ISafeTextReplacementService? safeTextReplacementService = null,
        RecentTextContextBuffer? recentTextContext = null,
        ILayoutConversionService? layoutConversionService = null,
        ISettingsService? settingsService = null,
        IKeyboardModifierState? keyboardModifierState = null,
        SerializedObservationQueue? observationQueue = null)
    {
        _inputMonitor = inputMonitor;
        _correctionUndoService = correctionUndoService;
        _safeTextReplacementService = safeTextReplacementService;
        _recentTextContext = recentTextContext;
        _layoutConversionService = layoutConversionService;
        _settingsService = settingsService;
        _keyboardModifierState = keyboardModifierState;
        _performanceMetrics = performanceMetrics;
        _logger = logger;
        _observationQueue = observationQueue ?? new SerializedObservationQueue();
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_initialized)
            {
                return Task.CompletedTask;
            }

            _inputMonitor.InputObserved += OnInputObserved;
            _initialized = true;
        }

        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_initialized)
            {
                return Task.CompletedTask;
            }

            _inputMonitor.InputObserved -= OnInputObserved;
            _initialized = false;
            ResetSequenceLocked();
            CancelPendingActionLocked();
            _controlIsDown = false;
            _altIsDown = false;
            _winIsDown = false;
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
    }

    private void OnInputObserved(object? sender, KeyboardObservationEventArgs observation)
    {
        // Live correction and Double Shift subscribe to the same monitor. If
        // they schedule independently, a Shift pair can read RecentTextContext
        // before the preceding character/boundary has reached Core. Use the
        // shared FIFO supplied by Runtime so manual toggle/undo observes the
        // exact same event order as live correction.
        _ = _observationQueue.Enqueue(() =>
        {
            ProcessObservedInput(observation);
            return Task.CompletedTask;
        });
    }

    private void ProcessObservedInput(KeyboardObservationEventArgs observation)
    {
        var action = DoubleShiftAction.None;
        CancellationTokenSource? actionCancellation = null;
        string? manualToken = null;
        string? manualReplacement = null;
        var manualContextVersion = -1L;

        lock (_sync)
        {
            if (!_initialized)
            {
                return;
            }

            UpdateBlockingModifierStateLocked(observation);

            if (observation.EventType == PlatformKeyEventType.KeyDown
                && !IsShift(observation.VirtualKeyCode))
            {
                // A command that has not reached SendInput yet must never
                // operate on text typed after the double Shift. This closes
                // the async gap that used to cause interleaved replacements.
                CancelPendingActionLocked();
            }

            if (observation.EventType == PlatformKeyEventType.KeyDown)
            {
                if (IsShift(observation.VirtualKeyCode))
                {
                    // Do not start replacement while the second Shift is
                    // physically held. The key-up below is the commit point;
                    // this prevents Shift from modifying the first Backspace
                    // or the Unicode payload of the replacement transaction.
                    ObserveShiftKeyDownLocked(observation.TimestampUtc);
                }
                else
                {
                    // A character or shortcut between taps means that this was
                    // ordinary Shift use, not a request to undo.
                    ResetSequenceLocked();
                }
            }
            else if (observation.EventType == PlatformKeyEventType.KeyUp && IsShift(observation.VirtualKeyCode))
            {
                action = ObserveShiftKeyUpLocked(
                    observation.TimestampUtc,
                    out manualToken,
                    out manualReplacement,
                    out manualContextVersion);
                if (action != DoubleShiftAction.None)
                {
                    actionCancellation = CreateActionCancellationLocked();
                }
            }
        }

        if (action == DoubleShiftAction.Undo && actionCancellation is not null)
        {
            _ = InvokeUndoAsync(actionCancellation);
        }
        else if (action == DoubleShiftAction.ManualLayoutFallback
            && actionCancellation is not null
            && manualToken is not null
            && manualReplacement is not null)
        {
            _ = InvokeManualLayoutFallbackAsync(
                actionCancellation,
                manualToken,
                manualReplacement,
                manualContextVersion);
        }
    }

    private void ObserveShiftKeyDownLocked(DateTimeOffset timestamp)
    {
        if (_shiftIsDown)
        {
            // Ignore OS key-repeat while the user holds Shift.
            return;
        }

        _shiftIsDown = true;
        if (HasBlockingModifierLocked())
        {
            ResetSequenceLocked();
            _shiftIsDown = true;
            return;
        }

        if (_firstShiftReleasedAt is not { } firstReleasedAt
            || timestamp - firstReleasedAt > GetDoubleShiftWindow())
        {
            _firstShiftReleasedAt = null;
            _secondTapInProgress = false;
            return;
        }

        _firstShiftReleasedAt = null;
        _secondTapInProgress = true;
    }

    private bool TryGetManualLayoutToggleLocked(
        out string token,
        out string replacement)
    {
        token = string.Empty;
        replacement = string.Empty;

        if (_safeTextReplacementService is null || _recentTextContext is null)
        {
            return false;
        }

        token = GetLastToken();
        if (string.IsNullOrEmpty(token) || _layoutConversionService is null)
        {
            return false;
        }

        var direction = IsLatinWord(token)
            ? LayoutConversionDirection.EnglishToRussian
            : IsCyrillicWord(token)
                ? LayoutConversionDirection.RussianToEnglish
                : (LayoutConversionDirection?)null;
        if (direction is null)
        {
            return false;
        }

        replacement = _layoutConversionService.Convert(token, direction.Value);
        return !string.IsNullOrEmpty(replacement)
            && !string.Equals(token, replacement, StringComparison.Ordinal);
    }

    private DoubleShiftAction ObserveShiftKeyUpLocked(
        DateTimeOffset timestamp,
        out string? manualToken,
        out string? manualReplacement,
        out long manualContextVersion)
    {
        manualToken = null;
        manualReplacement = null;
        manualContextVersion = -1L;

        if (!_shiftIsDown)
        {
            return DoubleShiftAction.None;
        }

        _shiftIsDown = false;
        if (_secondTapInProgress)
        {
            _secondTapInProgress = false;
            if (HasBlockingModifierLocked())
            {
                ResetSequenceLocked();
                return DoubleShiftAction.None;
            }

            if (_correctionUndoService.IsUndoAvailable)
            {
                return DoubleShiftAction.Undo;
            }

            if (TryGetManualLayoutToggleLocked(out var token, out var replacement))
            {
                manualToken = token;
                manualReplacement = replacement;
                manualContextVersion = _recentTextContext?.Version ?? -1L;
                return DoubleShiftAction.ManualLayoutFallback;
            }

            return DoubleShiftAction.None;
        }

        if (!HasBlockingModifierLocked())
        {
            _firstShiftReleasedAt = timestamp;
        }
        else
        {
            ResetSequenceLocked();
        }

        return DoubleShiftAction.None;
    }

    private void UpdateBlockingModifierStateLocked(KeyboardObservationEventArgs observation)
    {
        var isDown = observation.EventType == PlatformKeyEventType.KeyDown;
        switch (observation.VirtualKeyCode)
        {
            case VirtualKeys.Control:
                _controlIsDown = isDown;
                break;
            case VirtualKeys.Menu:
                _altIsDown = isDown;
                break;
            case VirtualKeys.LWin:
            case VirtualKeys.RWin:
                _winIsDown = isDown;
                break;
        }
    }

    private bool HasBlockingModifierLocked() => _controlIsDown || _altIsDown || _winIsDown;

    private void ResetSequenceLocked()
    {
        _firstShiftReleasedAt = null;
        _secondTapInProgress = false;
    }

    private async Task InvokeUndoAsync(CancellationTokenSource actionCancellation)
    {
        if (Interlocked.CompareExchange(ref _undoInvocationGate, 1, 0) != 0)
        {
            CompleteActionCancellation(actionCancellation);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var result = await _correctionUndoService
                .TryUndoAsync(actionCancellation.Token)
                .ConfigureAwait(false);
            if (result.Outcome == CorrectionUndoOutcome.Success)
            {
                // The injected undo is intentionally filtered from the live
                // token pipeline. Clear the local snapshot so it cannot be
                // mistaken for the current word by a later Double Shift.
                _recentTextContext?.Clear();
            }
            _logger.LogInformation("Double Shift undo completed with outcome={Outcome}.", result.Outcome);
        }
        catch (OperationCanceledException) when (actionCancellation.IsCancellationRequested)
        {
            _logger.LogDebug("Double Shift undo was cancelled by subsequent user input.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Double Shift undo invocation failed.");
        }
        finally
        {
            _performanceMetrics.RecordDuration(
                PerformanceMetricKind.HotkeyHandling,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            CompleteActionCancellation(actionCancellation);
            Interlocked.Exchange(ref _undoInvocationGate, 0);
        }
    }

    private async Task InvokeManualLayoutFallbackAsync(
        CancellationTokenSource actionCancellation,
        string token,
        string replacement,
        long expectedContextVersion)
    {
        if (_safeTextReplacementService is null || _recentTextContext is null)
        {
            CompleteActionCancellation(actionCancellation);
            return;
        }

        try
        {
            if (_recentTextContext.Version != expectedContextVersion)
            {
                return;
            }

            await WaitForPhysicalShiftReleaseAsync(actionCancellation.Token)
                .ConfigureAwait(false);

            actionCancellation.Token.ThrowIfCancellationRequested();
            var result = await _safeTextReplacementService
                .ReplaceRecentTextAsync(token, replacement, actionCancellation.Token)
                .ConfigureAwait(false);
            if (result.Status == SmartInput.Platform.Abstractions.Text.TextReplacementStatus.Success)
            {
                _recentTextContext.Clear();
            }

            _logger.LogInformation("Double Shift manual layout fallback completed with outcome={Outcome}.", result.Status);
        }
        catch (OperationCanceledException) when (actionCancellation.IsCancellationRequested)
        {
            _logger.LogDebug("Double Shift manual layout toggle was cancelled by subsequent user input.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Double Shift manual layout fallback failed.");
        }
        finally
        {
            CompleteActionCancellation(actionCancellation);
        }
    }

    private async Task WaitForPhysicalShiftReleaseAsync(CancellationToken cancellationToken)
    {
        if (_keyboardModifierState is null)
        {
            return;
        }

        var deadline = Stopwatch.GetTimestamp()
            + (long)(Stopwatch.Frequency * 0.250);
        while (_keyboardModifierState.IsShiftDown)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetTimestamp() >= deadline)
            {
                // Never send Backspace/Unicode while Shift is still down. A
                // failed manual toggle is safer than leaving a partial token
                // such as a leading character before the converted word.
                throw new OperationCanceledException(cancellationToken);
            }

            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }

        // Give the input queue one scheduling turn after GetAsyncKeyState
        // reports release, so the target application cannot observe the first
        // injected Backspace under the previous modifier state.
        await Task.Delay(5, cancellationToken).ConfigureAwait(false);
    }

    private string GetLastToken()
    {
        if (_recentTextContext is null)
        {
            return string.Empty;
        }

        // Prefer the token captured synchronously at the boundary. The rolling
        // snapshot may be shorter when hook events and the async coordinator
        // cross a focus transition; using it would delete only the tail and
        // leave a leading character before the manual replacement.
        return _recentTextContext.LastCompletedToken
            ?? ExtractLastToken(_recentTextContext.Snapshot());
    }

    private static string ExtractLastToken(string text)
    {
        var end = text.Length;
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        var start = end;
        while (start > 0 && char.IsLetter(text[start - 1]))
        {
            start--;
        }

        if (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            return string.Empty;
        }

        return start < end ? text[start..end] : string.Empty;
    }

    private static bool IsLatinWord(string token)
        => token.All(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    private static bool IsCyrillicWord(string token)
        => token.All(static character => character is >= 'А' and <= 'я' or 'Ё' or 'ё');

    private CancellationTokenSource CreateActionCancellationLocked()
    {
        CancelPendingActionLocked();
        var cancellation = new CancellationTokenSource();
        _pendingActionCancellation = cancellation;
        return cancellation;
    }

    private void CancelPendingActionLocked()
    {
        _pendingActionCancellation?.Cancel();
        _pendingActionCancellation = null;
    }

    private void CompleteActionCancellation(CancellationTokenSource cancellation)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_pendingActionCancellation, cancellation))
            {
                _pendingActionCancellation = null;
            }
        }

        cancellation.Dispose();
    }

    private enum DoubleShiftAction
    {
        None,
        Undo,
        ManualLayoutFallback,
    }

    private static bool IsShift(int virtualKeyCode) => virtualKeyCode is VirtualKeys.Shift
        or VirtualKeys.LShift
        or VirtualKeys.RShift;

    private TimeSpan GetDoubleShiftWindow()
    {
        var configured = _settingsService?.Current.DoubleShiftWindowMilliseconds
            ?? DefaultDoubleShiftWindowMilliseconds;
        var bounded = Math.Clamp(
            configured,
            MinimumDoubleShiftWindowMilliseconds,
            MaximumDoubleShiftWindowMilliseconds);
        return TimeSpan.FromMilliseconds(bounded);
    }
}
