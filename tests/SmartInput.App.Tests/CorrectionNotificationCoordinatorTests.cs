using Microsoft.Extensions.Logging.Abstractions;
using SmartInput.App.Services;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.App.Tests;

public class CorrectionNotificationCoordinatorTests
{
    [Fact]
    public async Task NotifySuccessfulCorrection_ShowsOverlayNearCaret()
    {
        var overlay = new RecordingCorrectionNotificationOverlayService();
        var coordinator = CreateCoordinator(
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)));

        await coordinator.InitializeAsync();
        coordinator.NotifySuccessfulCorrection(CorrectionKind.Layout);
        await Task.Delay(50);

        Assert.Equal(1, overlay.ShowCallCount);
        Assert.Equal(CorrectionNotificationMessages.Layout, overlay.LastContent?.Message);
        Assert.NotNull(overlay.LastPlacement);
    }

    [Fact]
    public async Task NotifySuccessfulCorrection_MissingCaret_DoesNotShow()
    {
        var overlay = new RecordingCorrectionNotificationOverlayService();
        var coordinator = CreateCoordinator(
            overlay,
            new FakeCaretPositionService(null));

        await coordinator.InitializeAsync();
        coordinator.NotifySuccessfulCorrection(CorrectionKind.Autocorrect);
        await Task.Delay(50);

        Assert.Equal(0, overlay.ShowCallCount);
    }

    [Fact]
    public async Task NotifyUserInput_HidesVisibleOverlay()
    {
        var overlay = new RecordingCorrectionNotificationOverlayService
        {
            Status = new CorrectionNotificationStatus(CorrectionNotificationVisibility.Visible, true),
        };
        var coordinator = CreateCoordinator(
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)));

        await coordinator.InitializeAsync();
        coordinator.NotifyUserInput();
        await Task.Delay(50);

        Assert.Equal(1, overlay.HideCallCount);
    }

    [Fact]
    public async Task NotifyCorrectionUndone_HidesVisibleOverlay()
    {
        var overlay = new RecordingCorrectionNotificationOverlayService
        {
            Status = new CorrectionNotificationStatus(CorrectionNotificationVisibility.Visible, true),
        };
        var coordinator = CreateCoordinator(
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)));

        await coordinator.InitializeAsync();
        coordinator.NotifyCorrectionUndone();
        await Task.Delay(50);

        Assert.Equal(1, overlay.HideCallCount);
    }

    [Fact]
    public async Task NotifySuccessfulCorrection_EmergencyPause_DoesNotShow()
    {
        var overlay = new RecordingCorrectionNotificationOverlayService();
        var coordinator = CreateCoordinator(
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)),
            emergencyPause: new FakeEmergencyPauseService(isPaused: true));

        await coordinator.InitializeAsync();
        coordinator.NotifySuccessfulCorrection(CorrectionKind.Snippet);
        await Task.Delay(50);

        Assert.Equal(0, overlay.ShowCallCount);
    }

    [Fact]
    public async Task NotifySuccessfulCorrection_ProtectionDisabled_DoesNotShow()
    {
        var overlay = new RecordingCorrectionNotificationOverlayService();
        var settings = EnabledSettings();
        settings.IsEnabled = false;
        var coordinator = CreateCoordinator(
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)),
            settings: new FakeSettingsService(settings));

        await coordinator.InitializeAsync();
        coordinator.NotifySuccessfulCorrection(CorrectionKind.Layout);
        await Task.Delay(50);

        Assert.Equal(0, overlay.ShowCallCount);
    }

    [Fact]
    public async Task NotifySuccessfulCorrection_SecureInputPolicy_DoesNotShow()
    {
        var overlay = new RecordingCorrectionNotificationOverlayService();
        var coordinator = CreateCoordinator(
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)),
            safety: new FakeAutomationSafetyService(SecurePolicy()));

        await coordinator.InitializeAsync();
        coordinator.NotifySuccessfulCorrection(CorrectionKind.Layout);
        await Task.Delay(50);

        Assert.Equal(0, overlay.ShowCallCount);
    }

    [Fact]
    public async Task DisposeAsync_ShutsDownOverlay()
    {
        var overlay = new RecordingCorrectionNotificationOverlayService();
        var coordinator = CreateCoordinator(
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)));

        await coordinator.InitializeAsync();
        await coordinator.DisposeAsync();

        Assert.Equal(1, overlay.ShutdownCallCount);
    }

    private static CorrectionNotificationCoordinator CreateCoordinator(
        RecordingCorrectionNotificationOverlayService overlay,
        FakeCaretPositionService caret,
        FakeAutomationSafetyService? safety = null,
        FakeSettingsService? settings = null,
        FakeEmergencyPauseService? emergencyPause = null)
    {
        return new CorrectionNotificationCoordinator(
            overlay,
            caret,
            new FakeActiveApplicationService(new ActiveApplicationInfo("notepad", "Untitled", "Notepad", 42)),
            safety ?? new FakeAutomationSafetyService(AllowedPolicy()),
            emergencyPause ?? new FakeEmergencyPauseService(false),
            settings ?? new FakeSettingsService(EnabledSettings()),
            NullLogger<CorrectionNotificationCoordinator>.Instance);
    }

    private static AppSettings EnabledSettings()
    {
        return new AppSettings
        {
            IsEnabled = true,
            AutomaticLayoutEnabled = true,
        };
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

    private static AutomationPolicyResult SecurePolicy()
    {
        return new AutomationPolicyResult
        {
            State = AutomationPolicyState.SecureInput,
            AllowsAutomation = false,
            AllowsManualExternalTextOperations = false,
        };
    }

    private sealed class RecordingCorrectionNotificationOverlayService : ICorrectionNotificationOverlayService
    {
        public int ShowCallCount { get; private set; }

        public int HideCallCount { get; private set; }

        public int ShutdownCallCount { get; private set; }

        public CorrectionNotificationContent? LastContent { get; private set; }

        public PredictionOverlayPlacement? LastPlacement { get; private set; }

        public CorrectionNotificationStatus Status { get; set; } =
            new(CorrectionNotificationVisibility.Hidden, true);

        public Task ShowAsync(
            CorrectionNotificationContent content,
            PredictionOverlayPlacement placement,
            CancellationToken cancellationToken = default)
        {
            ShowCallCount++;
            LastContent = content;
            LastPlacement = placement;
            Status = new CorrectionNotificationStatus(CorrectionNotificationVisibility.Visible, true);
            return Task.CompletedTask;
        }

        public Task HideAsync(CancellationToken cancellationToken = default)
        {
            HideCallCount++;
            Status = new CorrectionNotificationStatus(CorrectionNotificationVisibility.Hidden, true);
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ShutdownCallCount++;
            Status = new CorrectionNotificationStatus(CorrectionNotificationVisibility.Hidden, false);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCaretPositionService(CaretScreenPosition? position) : ICaretPositionService
    {
        public Task<CaretScreenPosition?> GetCaretScreenPositionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(position);
    }

    private sealed class FakeActiveApplicationService(ActiveApplicationInfo info) : IActiveApplicationService
    {
        public Task<ActiveApplicationInfo?> GetActiveApplicationAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ActiveApplicationInfo?>(info);
    }

    private sealed class FakeAutomationSafetyService(AutomationPolicyResult policy) : IAutomationSafetyService
    {
        public AutomationPolicyResult EvaluateCurrentContext() => policy;

        public Task<AutomationPolicyResult> EvaluateCurrentContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(policy);

        public bool IsOperationAllowed(AutomationOperationKind operationKind) => policy.IsAllowed(operationKind);

        public Task<bool> IsOperationAllowedAsync(
            AutomationOperationKind operationKind,
            CancellationToken cancellationToken = default)
            => Task.FromResult(policy.IsAllowed(operationKind));

        public string? GetBlockedReason(AutomationOperationKind operationKind)
            => policy.IsAllowed(operationKind) ? null : policy.Reason;
    }

    private sealed class FakeEmergencyPauseService(bool isPaused) : IEmergencyPauseService
    {
        public bool IsPaused { get; private set; } = isPaused;

        public void Pause() => IsPaused = true;

        public void Resume() => IsPaused = false;
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
