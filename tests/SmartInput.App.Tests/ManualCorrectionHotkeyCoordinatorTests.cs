using SmartInput.App.Services;
using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Hotkeys;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.App.Tests;

public class ManualCorrectionHotkeyCoordinatorTests
{
    [Theory]
    [InlineData(HotkeyDefaults.FixLayoutEnToRuHotkey)]
    [InlineData(HotkeyDefaults.FixLayoutRuToEnHotkey)]
    [InlineData(HotkeyDefaults.FixSpellingHotkey)]
    [InlineData(HotkeyDefaults.FixTextEnToRuHotkey)]
    [InlineData(HotkeyDefaults.FixTextRuToEnHotkey)]
    public void DefaultManualCorrectionHotkeys_ParseAsValid(string combination)
    {
        var binding = HotkeyBindingParser.Parse(combination);

        Assert.True(binding.IsValid);
        Assert.Equal(HotkeyValidationState.Valid, binding.ValidationState);
    }

    [Fact]
    public async Task InitializeAsync_RegistersAllConfiguredHotkeys()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = new RecordingManualCorrectionService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();

        Assert.Equal(ManualCorrectionHotkeyCatalog.Definitions.Count, hotkeys.RegisterCallCount);
        Assert.All(ManualCorrectionHotkeyCatalog.Definitions, definition =>
            Assert.True(coordinator.RegistrationStatuses[definition.HotkeyId]!.IsSuccess));
    }

    [Fact]
    public async Task ApplyBindingAsync_ReRegistersOnSettingsChange()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = new RecordingManualCorrectionService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        var result = await coordinator.ApplyBindingAsync(
            HotkeyDefaults.FixSpellingId,
            "Ctrl+Alt+Shift+P");

        Assert.True(result.IsSuccess);
        Assert.Equal("Ctrl+Alt+Shift+P", settings.Current.FixSpellingHotkey);
        Assert.Equal(ManualCorrectionHotkeyCatalog.Definitions.Count + 1, hotkeys.RegisterCallCount);
    }

    [Fact]
    public async Task ApplyBindingAsync_EmptyBinding_DisablesHotkey()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = new RecordingManualCorrectionService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        var result = await coordinator.ApplyBindingAsync(HotkeyDefaults.FixSpellingId, string.Empty);

        Assert.Equal(GlobalHotkeyRegistrationState.NotRegistered, result.State);
        Assert.Equal(string.Empty, settings.Current.FixSpellingHotkey);
        Assert.True(hotkeys.UnregisterCallCount >= 1);
    }

    [Fact]
    public async Task ApplyBindingAsync_WhenConflict_ReportsPerBindingStatus()
    {
        var hotkeys = new FakeGlobalHotkeyService
        {
            NextResultFactory = (id, display) => id == HotkeyDefaults.FixSpellingId
                ? GlobalHotkeyRegistrationResult.Conflict(
                    id,
                    display,
                    $"Hotkey '{display}' is already registered by another application.")
                : GlobalHotkeyRegistrationResult.Registered(id, display),
        };
        var manual = new RecordingManualCorrectionService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();

        Assert.Equal(GlobalHotkeyRegistrationState.Conflict, coordinator.RegistrationStatuses[HotkeyDefaults.FixSpellingId]!.State);
        Assert.True(coordinator.RegistrationStatuses[HotkeyDefaults.FixLayoutEnToRuId]!.IsSuccess);
    }

    [Fact]
    public async Task ApplyBindingAsync_InvalidBinding_UnregistersAndReportsInvalid()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = new RecordingManualCorrectionService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        var result = await coordinator.ApplyBindingAsync(HotkeyDefaults.FixLayoutEnToRuId, "Ctrl+Alt");

        Assert.Equal(GlobalHotkeyRegistrationState.InvalidBinding, result.State);
        Assert.True(hotkeys.UnregisterCallCount >= 1);
    }

    [Fact]
    public async Task ShutdownAsync_UnregistersAllHotkeys()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = new RecordingManualCorrectionService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        await coordinator.ShutdownAsync();

        Assert.Equal(ManualCorrectionHotkeyCatalog.Definitions.Count, hotkeys.UnregisterCallCount);
        Assert.All(coordinator.RegistrationStatuses.Values, status => Assert.Null(status));
    }

    [Fact]
    public async Task DisposeAsync_DoesNotDisposeSharedPlatformService()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = new RecordingManualCorrectionService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        await coordinator.DisposeAsync();

        Assert.False(hotkeys.Disposed);
        Assert.Equal(ManualCorrectionHotkeyCatalog.Definitions.Count, hotkeys.UnregisterCallCount);
    }

    [Theory]
    [InlineData(HotkeyDefaults.FixLayoutEnToRuId, ManualCorrectionActionKind.FixLayout, LayoutConversionDirection.EnglishToRussian)]
    [InlineData(HotkeyDefaults.FixLayoutRuToEnId, ManualCorrectionActionKind.FixLayout, LayoutConversionDirection.RussianToEnglish)]
    [InlineData(HotkeyDefaults.FixSpellingId, ManualCorrectionActionKind.FixSpelling, null)]
    [InlineData(HotkeyDefaults.FixTextEnToRuId, ManualCorrectionActionKind.FixText, LayoutConversionDirection.EnglishToRussian)]
    [InlineData(HotkeyDefaults.FixTextRuToEnId, ManualCorrectionActionKind.FixText, LayoutConversionDirection.RussianToEnglish)]
    public async Task HotkeyPressed_InvokesCorrectManualActionOnce(
        string hotkeyId,
        ManualCorrectionActionKind expectedAction,
        LayoutConversionDirection? expectedDirection)
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = new RecordingManualCorrectionService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        hotkeys.RaisePressed(hotkeyId);
        await Task.Delay(50);

        Assert.Equal(1, manual.ExecuteCallCount);
        Assert.Equal(expectedAction, manual.LastRequest!.Action);
        Assert.Equal(expectedDirection, manual.LastRequest.LayoutDirection);
    }

    [Fact]
    public async Task HotkeyPressed_WhenOperationAlreadyActive_DoesNotStartSecondOperation()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = new RecordingManualCorrectionService
        {
            DelayCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        hotkeys.RaisePressed(HotkeyDefaults.FixSpellingId);
        hotkeys.RaisePressed(HotkeyDefaults.FixLayoutEnToRuId);
        await Task.Delay(50);

        Assert.Equal(1, manual.ExecuteCallCount);

        manual.DelayCompletion.SetResult();
        await Task.Delay(50);
    }

    [Fact]
    public async Task HotkeyPressed_WithSafeModePolicy_AllowsManualInvocation()
    {
        var selected = new FakeSelectedTextService("превет");
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = CreateManualService(selected, SafeModePolicy());
        var settings = new FakeSettingsService(new AppSettings { IsEnabled = true });
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        hotkeys.RaisePressed(HotkeyDefaults.FixSpellingId);
        await Task.Delay(50);

        Assert.Equal(1, selected.ReadCount);
        Assert.Equal("привет", selected.LastReplacement);
    }

    [Theory]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    [InlineData(AutomationPolicyState.BlockedApplication)]
    public async Task HotkeyPressed_InBlockedPolicyContext_DoesNotReplaceSelection(AutomationPolicyState state)
    {
        var selected = new FakeSelectedTextService("превет");
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = CreateManualService(
            selected,
            new AutomationPolicyResult
            {
                State = state,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = false,
                Reason = state.ToString(),
            });
        var settings = new FakeSettingsService(new AppSettings { IsEnabled = true });
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        hotkeys.RaisePressed(HotkeyDefaults.FixSpellingId);
        await Task.Delay(50);

        Assert.Equal(0, selected.ReadCount);
        Assert.Null(selected.LastReplacement);
    }

    [Fact]
    public async Task HotkeyPressed_WhenEmergencyPaused_IsBlocked()
    {
        var selected = new FakeSelectedTextService("превет");
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = CreateManualService(selected, AutomationPolicyResult.EmergencyPaused());
        var settings = new FakeSettingsService(new AppSettings { IsEnabled = true });
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        hotkeys.RaisePressed(HotkeyDefaults.FixSpellingId);
        await Task.Delay(50);

        Assert.Equal(0, selected.ReadCount);
        Assert.Null(selected.LastReplacement);
    }

    [Fact]
    public async Task HotkeyPressed_WhenProtectionDisabled_IsBlocked()
    {
        var selected = new FakeSelectedTextService("превет");
        var hotkeys = new FakeGlobalHotkeyService();
        var manual = CreateManualService(
            selected,
            AllowedPolicy(),
            settings: new AppSettings { IsEnabled = false });
        var settings = new FakeSettingsService(new AppSettings { IsEnabled = false });
        var coordinator = CreateCoordinator(hotkeys, manual, settings);

        await coordinator.InitializeAsync();
        hotkeys.RaisePressed(HotkeyDefaults.FixSpellingId);
        await Task.Delay(50);

        Assert.Equal(0, selected.ReadCount);
        Assert.Null(selected.LastReplacement);
    }

    private static ManualCorrectionHotkeyCoordinator CreateCoordinator(
        FakeGlobalHotkeyService hotkeys,
        IManualSelectedTextCorrectionService manual,
        FakeSettingsService settings)
    {
        return new ManualCorrectionHotkeyCoordinator(
            hotkeys,
            manual,
            settings,
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ManualCorrectionHotkeyCoordinator>.Instance);
    }

    private static ManualSelectedTextCorrectionService CreateManualService(
        FakeSelectedTextService selectedText,
        AutomationPolicyResult policy,
        AppSettings? settings = null)
    {
        return new ManualSelectedTextCorrectionService(
            new KeyboardLayoutConverter(),
            selectedText,
            new AutocorrectionService(),
            CreateStarterDictionary(),
            new StaticSafetyService(policy),
            new FakeSettingsService(settings ?? new AppSettings { IsEnabled = true }));
    }

    private static CompositeAutocorrectDictionary CreateStarterDictionary()
    {
        return new CompositeAutocorrectDictionary(new EmptyUserDictionaryStore());
    }

    private static AutomationPolicyResult AllowedPolicy()
    {
        return new AutomationPolicyResult
        {
            State = AutomationPolicyState.Allowed,
            AllowsAutomation = true,
            AllowsManualExternalTextOperations = true,
        };
    }

    private static AutomationPolicyResult SafeModePolicy()
    {
        return new AutomationPolicyResult
        {
            State = AutomationPolicyState.SafeMode,
            AllowsAutomation = false,
            AllowsManualExternalTextOperations = true,
            Reason = "Safe Mode is active for this application category.",
        };
    }

    private sealed class RecordingManualCorrectionService : IManualSelectedTextCorrectionService
    {
        public TaskCompletionSource? DelayCompletion { get; set; }

        public int ExecuteCallCount { get; private set; }

        public ManualCorrectionRequest? LastRequest { get; private set; }

        public ManualCorrectionResult? LastResult { get; private set; }

        public async Task<ManualCorrectionResult> ExecuteAsync(
            ManualCorrectionRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;
            LastRequest = request;

            if (DelayCompletion is not null)
            {
                await DelayCompletion.Task.ConfigureAwait(false);
            }

            LastResult = ManualCorrectionResult.NoSelection();
            return LastResult;
        }
    }

    private sealed class FakeGlobalHotkeyService : IGlobalHotkeyService
    {
        private readonly Dictionary<string, GlobalHotkeyRegistrationResult> _registrations = new(StringComparer.Ordinal);

        public Func<string, string, GlobalHotkeyRegistrationResult>? NextResultFactory { get; set; }

        public int RegisterCallCount { get; private set; }

        public int UnregisterCallCount { get; private set; }

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

    private sealed class FakeSelectedTextService(string? selectedText) : ISelectedTextService
    {
        public int ReadCount { get; private set; }

        public string? LastReplacement { get; private set; }

        public Task<string?> GetSelectedTextAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(selectedText);
        }

        public Task<bool> ReplaceSelectedTextAsync(string replacementText, CancellationToken cancellationToken = default)
        {
            LastReplacement = replacementText;
            return Task.FromResult(true);
        }
    }

    private sealed class StaticSafetyService(AutomationPolicyResult policy) : IAutomationSafetyService
    {
        public AutomationPolicyResult EvaluateCurrentContext() => policy;

        public Task<AutomationPolicyResult> EvaluateCurrentContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(policy);
        }

        public bool IsOperationAllowed(AutomationOperationKind operationKind) => policy.IsAllowed(operationKind);

        public Task<bool> IsOperationAllowedAsync(
            AutomationOperationKind operationKind,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(policy.IsAllowed(operationKind));
        }

        public string? GetBlockedReason(AutomationOperationKind operationKind)
        {
            return policy.IsAllowed(operationKind) ? null : policy.Reason;
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

    private sealed class EmptyUserDictionaryStore : IUserAutocorrectDictionaryStore
    {
        public IReadOnlyList<UserAutocorrectDictionaryEntry> Entries { get; } = [];

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
