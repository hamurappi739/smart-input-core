using Microsoft.Extensions.Logging.Abstractions;
using SmartInput.Core.Engines;
using SmartInput.App.Services;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;
using PlatformKeyEventType = SmartInput.Platform.Abstractions.Input.KeyEventType;

namespace SmartInput.App.Tests;

public class DoubleShiftUndoCoordinatorTests
{
    [Fact]
    public async Task TwoQuickReleasedShifts_InvokeUndoOnce()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = true };
        await using var coordinator = CreateCoordinator(input, undo);
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(50));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(180));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(220));

        await WaitForUndoAsync(undo);
        Assert.Equal(1, undo.TryUndoCallCount);
    }

    [Fact]
    public async Task ShiftHeldDown_DoesNotCountAsTwoTaps()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = true };
        await using var coordinator = CreateCoordinator(input, undo);
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(100));
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(150));

        await Task.Delay(30);
        Assert.Equal(0, undo.TryUndoCallCount);
    }

    [Fact]
    public async Task CharacterBetweenTaps_CancelsUndo()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = true };
        await using var coordinator = CreateCoordinator(input, undo);
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(40));
        input.Raise(0x41, PlatformKeyEventType.KeyDown, start.AddMilliseconds(80));
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(120));
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(160));

        await Task.Delay(30);
        Assert.Equal(0, undo.TryUndoCallCount);
    }

    [Fact]
    public async Task CtrlPlusDoubleShift_DoesNotInvokeUndo()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = true };
        await using var coordinator = CreateCoordinator(input, undo);
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.Control, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(20));
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(50));
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(100));
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(130));
        input.Raise(VirtualKeys.Control, PlatformKeyEventType.KeyUp, start.AddMilliseconds(150));

        await Task.Delay(30);
        Assert.Equal(0, undo.TryUndoCallCount);
    }

    [Fact]
    public async Task ExpiredSecondTap_DoesNotInvokeUndo()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = true };
        await using var coordinator = CreateCoordinator(input, undo);
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(20));
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(700));
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(720));

        await Task.Delay(30);
        Assert.Equal(0, undo.TryUndoCallCount);
    }

    [Fact]
    public async Task Shutdown_UnsubscribesFromInput()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = true };
        await using var coordinator = CreateCoordinator(input, undo);
        await coordinator.InitializeAsync();
        await coordinator.ShutdownAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(20));
        input.Raise(VirtualKeys.Shift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(80));

        await Task.Delay(30);
        Assert.Equal(0, undo.TryUndoCallCount);
    }

    [Fact]
    public async Task DoubleShift_WhenNoUndoAvailable_ConvertsKnownRussianLayoutFallback()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = false };
        var replacement = new RecordingReplacementService();
        var context = new RecentTextContextBuffer();
        foreach (var character in "пзе ")
        {
            context.Apply(TokenInputEvent.CharacterInput(character));
        }

        await using var coordinator = new DoubleShiftUndoCoordinator(
            input,
            undo,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            NullLogger<DoubleShiftUndoCoordinator>.Instance,
            replacement,
            context,
            new KeyboardLayoutConverter());
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(30));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(120));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(150));

        for (var attempt = 0; attempt < 20 && replacement.CallCount == 0; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("пзе", replacement.OriginalText);
        Assert.Equal("gpt", replacement.ReplacementText);
    }

    [Fact]
    public async Task DoubleShift_ManualFallbackStartsOnlyAfterSecondShiftIsReleased()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = false };
        var replacement = new RecordingReplacementService();
        var context = new RecentTextContextBuffer();
        foreach (var character in "пзе ")
        {
            context.Apply(TokenInputEvent.CharacterInput(character));
        }

        await using var coordinator = new DoubleShiftUndoCoordinator(
            input,
            undo,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            NullLogger<DoubleShiftUndoCoordinator>.Instance,
            replacement,
            context,
            new KeyboardLayoutConverter());
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(20));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(100));

        await Task.Delay(20);
        Assert.Equal(0, replacement.CallCount);

        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(120));
        for (var attempt = 0; attempt < 20 && replacement.CallCount == 0; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("пзе", replacement.OriginalText);
        Assert.Equal("gpt", replacement.ReplacementText);
    }

    [Fact]
    public async Task DoubleShift_ManualFallbackUsesHookCompletedTokenWhenRollingSnapshotIsShort()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = false };
        var replacement = new RecordingReplacementService();
        var context = new RecentTextContextBuffer();
        foreach (var character in "зе ")
        {
            context.Apply(TokenInputEvent.CharacterInput(character));
        }

        // This models a queue/focus transition in which the async rolling
        // buffer lost the first character while hook-thread preflight retained
        // the complete token.
        context.RememberCompletedToken("пзе");

        await using var coordinator = new DoubleShiftUndoCoordinator(
            input,
            undo,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            NullLogger<DoubleShiftUndoCoordinator>.Instance,
            replacement,
            context,
            new KeyboardLayoutConverter());
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(20));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(100));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(120));

        for (var attempt = 0; attempt < 20 && replacement.CallCount == 0; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("пзе", replacement.OriginalText);
        Assert.Equal("gpt", replacement.ReplacementText);
    }

    [Fact]
    public async Task DoubleShift_UsesSharedQueueAfterEarlierLiveObservationCompletes()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = false };
        var replacement = new RecordingReplacementService();
        var context = new RecentTextContextBuffer();
        var queue = new SerializedObservationQueue();
        var releaseLiveObservation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Model the live coordinator still finishing the preceding token. A
        // Double Shift subscriber using the same queue must not inspect the
        // old/partial snapshot before this operation completes.
        var liveObservation = queue.Enqueue(async () =>
        {
            await releaseLiveObservation.Task;
            foreach (var character in "пзе ")
            {
                context.Apply(TokenInputEvent.CharacterInput(character));
            }
        });

        await using var coordinator = new DoubleShiftUndoCoordinator(
            input,
            undo,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            NullLogger<DoubleShiftUndoCoordinator>.Instance,
            replacement,
            context,
            new KeyboardLayoutConverter(),
            observationQueue: queue);
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(20));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(100));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(120));

        await Task.Delay(25);
        Assert.Equal(0, replacement.CallCount);

        releaseLiveObservation.SetResult(true);
        await liveObservation;
        for (var attempt = 0; attempt < 30 && replacement.CallCount == 0; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("пзе", replacement.OriginalText);
        Assert.Equal("gpt", replacement.ReplacementText);
    }

    [Fact]
    public async Task DoubleShift_ManualFallbackWaitsForPhysicalShiftStateToClear()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = false };
        var replacement = new RecordingReplacementService();
        var context = new RecentTextContextBuffer();
        foreach (var character in "пзе ")
        {
            context.Apply(TokenInputEvent.CharacterInput(character));
        }

        var modifierState = new FakeKeyboardModifierState { IsShiftDown = true };
        await using var coordinator = new DoubleShiftUndoCoordinator(
            input,
            undo,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            NullLogger<DoubleShiftUndoCoordinator>.Instance,
            replacement,
            context,
            new KeyboardLayoutConverter(),
            keyboardModifierState: modifierState);
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(20));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(100));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(120));

        await Task.Delay(35);
        Assert.Equal(0, replacement.CallCount);

        modifierState.IsShiftDown = false;
        for (var attempt = 0; attempt < 30 && replacement.CallCount == 0; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("пзе", replacement.OriginalText);
        Assert.Equal("gpt", replacement.ReplacementText);
    }

    [Fact]
    public async Task DoubleShift_WhenNoUndoAvailable_ConvertsAnyEnglishWordToRussianLayout()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = false };
        var replacement = new RecordingReplacementService();
        var context = new RecentTextContextBuffer();
        foreach (var character in "ghbdtn ")
        {
            context.Apply(TokenInputEvent.CharacterInput(character));
        }

        await using var coordinator = new DoubleShiftUndoCoordinator(
            input,
            undo,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            NullLogger<DoubleShiftUndoCoordinator>.Instance,
            replacement,
            context,
            new KeyboardLayoutConverter());
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(30));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(120));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(150));

        for (var attempt = 0; attempt < 20 && replacement.CallCount == 0; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("ghbdtn", replacement.OriginalText);
        Assert.Equal("привет", replacement.ReplacementText);
    }

    [Fact]
    public async Task DoubleShift_DoesNotToggleUrlTail()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = false };
        var replacement = new RecordingReplacementService();
        var context = new RecentTextContextBuffer();
        foreach (var character in "https://ghbdtn.com/test ")
        {
            context.Apply(TokenInputEvent.CharacterInput(character));
        }

        await using var coordinator = new DoubleShiftUndoCoordinator(
            input,
            undo,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            NullLogger<DoubleShiftUndoCoordinator>.Instance,
            replacement,
            context,
            new KeyboardLayoutConverter());
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(30));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(120));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(150));

        await Task.Delay(80);

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task DoubleShift_UsesConfiguredWindowAndCancelsWhenNextCharacterArrives()
    {
        var input = new FakeInputMonitor();
        var undo = new RecordingUndoService { IsAvailable = false };
        var replacement = new CancellationAwareReplacementService();
        var context = new RecentTextContextBuffer();
        foreach (var character in "ghbdtn ")
        {
            context.Apply(TokenInputEvent.CharacterInput(character));
        }

        var settings = new FakeSettingsService(new AppSettings
        {
            IsEnabled = true,
            DoubleShiftWindowMilliseconds = 200,
        });

        await using var coordinator = new DoubleShiftUndoCoordinator(
            input,
            undo,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            NullLogger<DoubleShiftUndoCoordinator>.Instance,
            replacement,
            context,
            new KeyboardLayoutConverter(),
            settings);
        await coordinator.InitializeAsync();

        var start = DateTimeOffset.UtcNow;
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyDown, start);
        input.Raise(VirtualKeys.LShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(20));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyDown, start.AddMilliseconds(100));
        input.Raise(VirtualKeys.RShift, PlatformKeyEventType.KeyUp, start.AddMilliseconds(120));

        await replacement.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        input.Raise(0x41, PlatformKeyEventType.KeyDown, start.AddMilliseconds(110));

        await replacement.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(TextReplacementStatus.AbortedByUserInput, replacement.Result?.Status);
    }

    private static DoubleShiftUndoCoordinator CreateCoordinator(
        FakeInputMonitor input,
        RecordingUndoService undo)
    {
        return new DoubleShiftUndoCoordinator(
            input,
            undo,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            NullLogger<DoubleShiftUndoCoordinator>.Instance);
    }

    private static async Task WaitForUndoAsync(RecordingUndoService undo)
    {
        for (var attempt = 0; attempt < 20 && undo.TryUndoCallCount == 0; attempt++)
        {
            await Task.Delay(10);
        }
    }

    private sealed class FakeInputMonitor : IInputMonitor
    {
        public bool IsMonitoring => true;

        public event EventHandler<KeyboardObservationEventArgs>? InputObserved;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Raise(int virtualKeyCode, PlatformKeyEventType eventType, DateTimeOffset timestamp) =>
            InputObserved?.Invoke(this, new KeyboardObservationEventArgs
            {
                VirtualKeyCode = virtualKeyCode,
                EventType = eventType,
                TimestampUtc = timestamp,
            });
    }

    private sealed class RecordingUndoService : ICorrectionUndoService
    {
        public bool IsAvailable { get; init; }

        public int TryUndoCallCount { get; private set; }

        public CorrectionUndoStatus Status { get; } = new();

        public bool IsUndoAvailable => IsAvailable;

        public CorrectionKind? PendingCorrectionKind => null;

        public void RecordSuccessfulCorrection(CorrectionTransaction transaction) { }

        public void Invalidate(CorrectionUndoInvalidationReason reason) { }

        public void NotifyGenuineUserInput() { }

        public void NotifyApplicationContextChanged(string? processName, nint windowHandle) { }

        public void NotifyPolicyContextChanged(AutomationPolicyResult policy) { }

        public Task<CorrectionUndoResult> TryUndoAsync(CancellationToken cancellationToken = default)
        {
            TryUndoCallCount++;
            return Task.FromResult(CorrectionUndoResult.Succeeded(CorrectionKind.Autocorrect));
        }
    }

    private sealed class FakeKeyboardModifierState : IKeyboardModifierState
    {
        public bool IsShiftDown { get; set; }
    }

    private sealed class RecordingReplacementService : ISafeTextReplacementService
    {
        public int CallCount { get; private set; }

        public string? OriginalText { get; private set; }

        public string? ReplacementText { get; private set; }

        public Task<TextReplacementResult> ReplaceRecentTextAsync(
            string originalText,
            string replacementText,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            OriginalText = originalText;
            ReplacementText = replacementText;
            return Task.FromResult(TextReplacementResult.Success(originalText.Length, replacementText.Length));
        }

        public Task<TextReplacementResult> InsertTextAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TextReplacementResult.Success(0, text.Length));

        public Task<TextReplacementResult> RunAbcToXyzDemoAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TextReplacementResult.Success(0, 0));
    }

    private sealed class CancellationAwareReplacementService : ISafeTextReplacementService
    {
        public int CallCount { get; private set; }

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TextReplacementResult? Result { get; private set; }

        public async Task<TextReplacementResult> ReplaceRecentTextAsync(
            string originalText,
            string replacementText,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Result = TextReplacementResult.AbortedByUserInput(
                    originalText.Length,
                    replacementText.Length);
                Cancelled.TrySetResult(true);
                return Result;
            }

            throw new InvalidOperationException("Replacement test unexpectedly completed without cancellation.");
        }

        public Task<TextReplacementResult> InsertTextAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TextReplacementResult.Success(0, text.Length));

        public Task<TextReplacementResult> RunAbcToXyzDemoAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TextReplacementResult.Success(3, 3));
    }

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Current { get; } = settings;

        public event Action? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }
    }
}
