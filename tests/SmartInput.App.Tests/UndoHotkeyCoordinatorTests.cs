using SmartInput.App.Services;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Hotkeys;

namespace SmartInput.App.Tests;

public class UndoHotkeyCoordinatorTests
{
    [Fact]
    public async Task InitializeAsync_RegistersConfiguredHotkey()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var undo = new RecordingUndoService();
        var settings = new FakeSettingsService(new AppSettings
        {
            UndoLastCorrectionHotkey = "Ctrl+Alt+Shift+Z",
        });
        var coordinator = CreateCoordinator(hotkeys, undo, settings);

        await coordinator.InitializeAsync();

        Assert.Equal(1, hotkeys.RegisterCallCount);
        Assert.Equal(HotkeyDefaults.UndoLastCorrectionId, hotkeys.LastRegisteredId);
        Assert.True(coordinator.RegistrationStatus!.IsSuccess);
        Assert.Equal("Ctrl+Alt+Shift+Z", coordinator.CurrentBindingDisplayName);
    }

    [Fact]
    public async Task ApplyBindingAsync_ReRegistersOnSettingsChange()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var undo = new RecordingUndoService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, undo, settings);

        await coordinator.InitializeAsync();
        var result = await coordinator.ApplyBindingAsync("Ctrl+Alt+U");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, hotkeys.RegisterCallCount);
        Assert.Equal("Ctrl+Alt+U", settings.Current.UndoLastCorrectionHotkey);
        Assert.Equal("Ctrl+Alt+U", coordinator.CurrentBindingDisplayName);
    }

    [Fact]
    public async Task ApplyBindingAsync_WhenConflict_KeepsAppRunningAndReportsStatus()
    {
        var hotkeys = new FakeGlobalHotkeyService
        {
            NextResultFactory = (id, display) => GlobalHotkeyRegistrationResult.Conflict(
                id,
                display,
                $"Hotkey '{display}' is already registered by another application."),
        };
        var undo = new RecordingUndoService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, undo, settings);

        await coordinator.InitializeAsync();

        Assert.Equal(GlobalHotkeyRegistrationState.Conflict, coordinator.RegistrationStatus!.State);
        Assert.Contains("already registered", coordinator.RegistrationStatus.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyBindingAsync_InvalidBinding_UnregistersAndReportsInvalid()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var undo = new RecordingUndoService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, undo, settings);

        await coordinator.InitializeAsync();
        var result = await coordinator.ApplyBindingAsync("Ctrl+Alt");

        Assert.Equal(GlobalHotkeyRegistrationState.InvalidBinding, result.State);
        Assert.Equal(1, hotkeys.UnregisterCallCount);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task HotkeyPressed_InvokesUndoServiceExactlyOnce()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var undo = new RecordingUndoService
        {
            Result = CorrectionUndoResult.Succeeded(CorrectionKind.Autocorrect),
        };
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, undo, settings);

        await coordinator.InitializeAsync();
        hotkeys.RaisePressed(HotkeyDefaults.UndoLastCorrectionId);
        await Task.Delay(50);

        Assert.Equal(1, undo.TryUndoCallCount);
    }

    [Fact]
    public async Task HotkeyPressed_WhenUndoUnavailable_StillCallsUndoOnce()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var undo = new RecordingUndoService
        {
            Result = CorrectionUndoResult.NotAvailable(),
        };
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, undo, settings);

        await coordinator.InitializeAsync();
        hotkeys.RaisePressed(HotkeyDefaults.UndoLastCorrectionId);
        await Task.Delay(50);

        Assert.Equal(1, undo.TryUndoCallCount);
        Assert.Equal(CorrectionUndoOutcome.NotAvailable, undo.LastOutcome);
    }

    [Fact]
    public async Task ShutdownAsync_UnregistersHotkey()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var undo = new RecordingUndoService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, undo, settings);

        await coordinator.InitializeAsync();
        await coordinator.ShutdownAsync();

        Assert.Equal(1, hotkeys.UnregisterCallCount);
        Assert.Null(coordinator.RegistrationStatus);
    }

    [Fact]
    public async Task DisposeAsync_CleansUpPlatformService()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var undo = new RecordingUndoService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, undo, settings);

        await coordinator.InitializeAsync();
        await coordinator.DisposeAsync();

        Assert.True(hotkeys.Disposed);
        Assert.Equal(1, hotkeys.UnregisterCallCount);
    }

    private static UndoHotkeyCoordinator CreateCoordinator(
        FakeGlobalHotkeyService hotkeys,
        RecordingUndoService undo,
        FakeSettingsService settings)
    {
        return new UndoHotkeyCoordinator(
            hotkeys,
            undo,
            settings,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UndoHotkeyCoordinator>.Instance);
    }

    private sealed class FakeGlobalHotkeyService : IGlobalHotkeyService
    {
        private readonly Dictionary<string, GlobalHotkeyRegistrationResult> _registrations = new(StringComparer.Ordinal);

        public Func<string, string, GlobalHotkeyRegistrationResult>? NextResultFactory { get; set; }

        public int RegisterCallCount { get; private set; }

        public int UnregisterCallCount { get; private set; }

        public string? LastRegisteredId { get; private set; }

        public bool Disposed { get; private set; }

        public event EventHandler<GlobalHotkeyPressedEventArgs>? HotkeyPressed;

        public Task<GlobalHotkeyRegistrationResult> RegisterAsync(
            string hotkeyId,
            uint modifiers,
            uint virtualKey,
            string displayName,
            CancellationToken cancellationToken = default)
        {
            RegisterCallCount++;
            LastRegisteredId = hotkeyId;
            var result = NextResultFactory?.Invoke(hotkeyId, displayName)
                ?? GlobalHotkeyRegistrationResult.Registered(hotkeyId, displayName);
            _registrations[hotkeyId] = result;
            return Task.FromResult(result);
        }

        public Task UnregisterAsync(string hotkeyId, CancellationToken cancellationToken = default)
        {
            UnregisterCallCount++;
            _registrations.Remove(hotkeyId);
            return Task.CompletedTask;
        }

        public Task UnregisterAllAsync(CancellationToken cancellationToken = default)
        {
            _registrations.Clear();
            return Task.CompletedTask;
        }

        public GlobalHotkeyRegistrationResult? GetRegistration(string hotkeyId)
        {
            return _registrations.TryGetValue(hotkeyId, out var result) ? result : null;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void RaisePressed(string hotkeyId)
        {
            HotkeyPressed?.Invoke(this, new GlobalHotkeyPressedEventArgs { HotkeyId = hotkeyId });
        }
    }

    private sealed class RecordingUndoService : ICorrectionUndoService
    {
        public CorrectionUndoResult Result { get; set; } = CorrectionUndoResult.NotAvailable();

        public int TryUndoCallCount { get; private set; }

        public CorrectionUndoOutcome? LastOutcome { get; private set; }

        public CorrectionUndoStatus Status { get; } = new();

        public bool IsUndoAvailable => false;

        public CorrectionKind? PendingCorrectionKind => null;

        public void RecordSuccessfulCorrection(CorrectionTransaction transaction)
        {
        }

        public void Invalidate(CorrectionUndoInvalidationReason reason)
        {
        }

        public void NotifyGenuineUserInput()
        {
        }

        public void NotifyApplicationContextChanged(string? processName, nint windowHandle)
        {
        }

        public void NotifyPolicyContextChanged(AutomationPolicyResult policy)
        {
        }

        public Task<CorrectionUndoResult> TryUndoAsync(CancellationToken cancellationToken = default)
        {
            TryUndoCallCount++;
            LastOutcome = Result.Outcome;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Current { get; private set; } = settings;

        public event Action? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            return Task.CompletedTask;
        }
    }
}
