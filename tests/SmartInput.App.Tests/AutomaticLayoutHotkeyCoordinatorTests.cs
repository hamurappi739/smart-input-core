using Microsoft.Extensions.Logging.Abstractions;
using SmartInput.App.Services;
using SmartInput.Core.Configuration;
using SmartInput.Core.Diagnostics;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Hotkeys;

namespace SmartInput.App.Tests;

public class AutomaticLayoutHotkeyCoordinatorTests
{
    [Fact]
    public async Task InitializeAndPress_RegistersDefaultAndTogglesOnlyAutomaticLayout()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var settings = new FakeSettingsService(new AppSettings
        {
            IsEnabled = true,
            AutomaticLayoutEnabled = true,
        });
        var coordinator = CreateCoordinator(hotkeys, settings);

        await coordinator.InitializeAsync();
        hotkeys.RaisePressed(HotkeyDefaults.ToggleAutomaticLayoutId);
        await Task.Delay(50);

        Assert.Equal(HotkeyDefaults.ToggleAutomaticLayoutId, hotkeys.LastRegisteredId);
        Assert.Equal(HotkeyDefaults.ToggleAutomaticLayoutHotkey, coordinator.CurrentBindingDisplayName);
        Assert.False(settings.Current.AutomaticLayoutEnabled);
        Assert.True(settings.Current.IsEnabled);
    }

    [Fact]
    public async Task ApplyBindingAsync_PersistsNewTwoKeyBinding()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        var settings = new FakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(hotkeys, settings);

        await coordinator.InitializeAsync();
        var result = await coordinator.ApplyBindingAsync("Alt+F12");

        Assert.True(result.IsSuccess);
        Assert.Equal("Alt+F12", settings.Current.ToggleAutomaticLayoutHotkey);
        Assert.Equal("Alt+F12", coordinator.CurrentBindingDisplayName);
    }

    private static AutomaticLayoutHotkeyCoordinator CreateCoordinator(
        FakeGlobalHotkeyService hotkeys,
        FakeSettingsService settings)
    {
        return new AutomaticLayoutHotkeyCoordinator(
            hotkeys,
            settings,
            NullPerformanceMetricsRecorder.Instance,
            NullLogger<AutomaticLayoutHotkeyCoordinator>.Instance);
    }

    private sealed class FakeGlobalHotkeyService : IGlobalHotkeyService
    {
        public string? LastRegisteredId { get; private set; }

        public event EventHandler<GlobalHotkeyPressedEventArgs>? HotkeyPressed;

        public Task<GlobalHotkeyRegistrationResult> RegisterAsync(
            string hotkeyId,
            uint modifiers,
            uint virtualKey,
            string displayName,
            CancellationToken cancellationToken = default)
        {
            LastRegisteredId = hotkeyId;
            return Task.FromResult(GlobalHotkeyRegistrationResult.Registered(hotkeyId, displayName));
        }

        public Task UnregisterAsync(string hotkeyId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UnregisterAllAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public GlobalHotkeyRegistrationResult? GetRegistration(string hotkeyId) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaisePressed(string hotkeyId)
        {
            HotkeyPressed?.Invoke(this, new GlobalHotkeyPressedEventArgs { HotkeyId = hotkeyId });
        }
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
