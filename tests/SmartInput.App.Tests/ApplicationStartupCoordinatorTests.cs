using System.Collections.Generic;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartInput.App.Services;
using SmartInput.App.ViewModels;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Hotkeys;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.App.Tests;

[Collection(AppTestCollection.Name)]
public class ApplicationStartupCoordinatorTests
{
    [Fact]
    public void PrepareMainWindow_AssignsMainWindowBeforeReturning()
    {
        var harness = CreateHarness();

        harness.Startup.PrepareMainWindow(harness.Desktop);

        Assert.True(harness.Startup.IsMainWindowPrepared);
        Assert.NotNull(harness.Desktop.MainWindow);
        Assert.Same(harness.Desktop.MainWindow, harness.Startup.MainWindow);
        Assert.Equal(1, harness.Bootstrapper.CreateCallCount);
    }

    [Fact]
    public void PrepareMainWindow_ShowsAndActivatesMainWindow()
    {
        var harness = CreateHarness();

        harness.Startup.PrepareMainWindow(harness.Desktop);

        Assert.Equal(1, harness.Presenter.ShowCallCount);
        Assert.Equal(1, harness.Presenter.ActivateCallCount);
        Assert.True(harness.Presenter.IsVisible);
    }

    [Fact]
    public void PrepareMainWindow_DoesNotCreateDuplicateWindows()
    {
        var harness = CreateHarness();

        harness.Startup.PrepareMainWindow(harness.Desktop);
        var firstWindow = harness.Desktop.MainWindow;
        harness.Startup.PrepareMainWindow(harness.Desktop);

        Assert.Equal(1, harness.Bootstrapper.CreateCallCount);
        Assert.Same(firstWindow, harness.Desktop.MainWindow);
    }

    [Fact]
    public void PrepareTrayOnly_DefersWindowUntilExplicitEnsure()
    {
        var harness = CreateHarness(trayOnlyMode: true);

        harness.Startup.PrepareTrayOnly(harness.Desktop);

        Assert.True(harness.Startup.IsTrayOnlyMode);
        Assert.False(harness.Startup.IsMainWindowPrepared);
        Assert.Null(harness.Desktop.MainWindow);
        Assert.Equal(0, harness.Bootstrapper.CreateCallCount);
        Assert.Equal(ShutdownMode.OnExplicitShutdown, harness.Desktop.ShutdownMode);

        harness.Startup.EnsureMainWindow();

        Assert.True(harness.Startup.IsMainWindowPrepared);
        Assert.Same(harness.Startup.MainWindow, harness.Desktop.MainWindow);
        Assert.Equal(1, harness.Bootstrapper.CreateCallCount);
    }

    [Fact]
    public async Task InitializeCoordinatorsAsync_RunsAfterMainWindowAttachment()
    {
        var harness = CreateHarness();
        harness.Startup.PrepareMainWindow(harness.Desktop);

        await harness.Startup.InitializeCoordinatorsAsync();

        Assert.True(harness.Startup.AreCoordinatorsInitialized);
        Assert.Equal(
            [
                nameof(IInputDiagnosticCoordinator),
                nameof(ILiveLayoutCorrectionCoordinator),
                nameof(ICorrectionNotificationCoordinator),
                nameof(IAutomaticLayoutHotkeyCoordinator),
                nameof(IDoubleShiftUndoCoordinator),
                nameof(ISystemTrayCoordinator),
            ],
            harness.InitializationOrder);
        Assert.Equal(1, harness.Tray.InitializeCallCount);
    }

    [Fact]
    public async Task InitializeCoordinatorsAsync_WhenTrayUnavailable_ConfiguresNormalClose()
    {
        var harness = CreateHarness(trayAvailable: false);
        harness.Startup.PrepareMainWindow(harness.Desktop);

        await harness.Startup.InitializeCoordinatorsAsync();

        Assert.False(harness.Shell.IsCloseToTrayEnabled);
        Assert.Equal(ShutdownMode.OnMainWindowClose, harness.Desktop.ShutdownMode);
    }

    [Fact]
    public async Task InitializeCoordinatorsAsync_WhenTrayAvailable_ConfiguresCloseToTray()
    {
        var harness = CreateHarness(trayAvailable: true);
        harness.Startup.PrepareMainWindow(harness.Desktop);

        await harness.Startup.InitializeCoordinatorsAsync();

        Assert.True(harness.Shell.IsCloseToTrayEnabled);
        Assert.Equal(ShutdownMode.OnExplicitShutdown, harness.Desktop.ShutdownMode);
    }

    [Fact]
    public async Task InitializeCoordinatorsAsync_WhenCoordinatorFails_FallsBackToNormalClose()
    {
        var harness = CreateHarness(failCoordinator: nameof(ILiveLayoutCorrectionCoordinator));
        harness.Startup.PrepareMainWindow(harness.Desktop);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Startup.InitializeCoordinatorsAsync());

        Assert.False(harness.Shell.IsCloseToTrayEnabled);
        Assert.False(harness.Startup.AreCoordinatorsInitialized);
        Assert.NotNull(harness.Desktop.MainWindow);
    }

    [Fact]
    public async Task PerformCleanupAsync_StillRunsExactlyOnce_AfterStartupRefactor()
    {
        var tray = new RecordingTrayCoordinator();
        var services = new ServiceCollection();
        services.AddSingleton<ISystemTrayCoordinator>(tray);

        var provider = services.BuildServiceProvider();
        var shutdown = new ApplicationShutdownCoordinator(
            provider,
            NullLogger<ApplicationShutdownCoordinator>.Instance);

        await shutdown.PerformCleanupAsync();
        await shutdown.PerformCleanupAsync();

        Assert.True(shutdown.CleanupPerformed);
        Assert.Equal(1, tray.DisposeCallCount);
    }

    private static StartupHarness CreateHarness(
        bool trayAvailable = true,
        string? failCoordinator = null,
        bool trayOnlyMode = false)
    {
        var desktop = new FakeDesktopApplicationHost();
        var presenter = new RecordingMainWindowPresenter();
        var bootstrapper = new RecordingMainWindowBootstrapper(presenter);
        var shell = new ApplicationShell();
        var initializationOrder = new List<string>();
        var tray = new RecordingStartupTrayCoordinator(trayAvailable, initializationOrder);

        var services = new ServiceCollection();
        services.AddSingleton<IInputDiagnosticCoordinator>(
            new RecordingInputDiagnosticCoordinator(initializationOrder));
        services.AddSingleton<ILiveLayoutCorrectionCoordinator>(
            new RecordingLayoutCoordinator(
                initializationOrder,
                failCoordinator == nameof(ILiveLayoutCorrectionCoordinator)));
        services.AddSingleton<ICorrectionNotificationCoordinator>(
            new RecordingCorrectionNotificationCoordinator(initializationOrder));
        services.AddSingleton<ILivePredictionCoordinator>(
            new RecordingPredictionCoordinator(initializationOrder));
        services.AddSingleton<ILivePredictionOverlayCoordinator>(
            new RecordingOverlayCoordinator(initializationOrder));
        services.AddSingleton<ILivePredictionTabAcceptanceCoordinator>(
            new RecordingTabAcceptanceCoordinator(initializationOrder));
        services.AddSingleton<ILivePredictionEscDismissalCoordinator>(
            new RecordingEscDismissalCoordinator(initializationOrder));
        services.AddSingleton<IAutomaticLayoutHotkeyCoordinator>(
            new RecordingAutomaticLayoutHotkeyCoordinator(initializationOrder));
        services.AddSingleton<IUndoHotkeyCoordinator>(
            new RecordingUndoHotkeyCoordinator(initializationOrder));
        services.AddSingleton<IDoubleShiftUndoCoordinator>(
            new RecordingDoubleShiftUndoCoordinator(initializationOrder));
        services.AddSingleton<IManualCorrectionHotkeyCoordinator>(
            new RecordingManualHotkeyCoordinator(initializationOrder));
        services.AddSingleton<ISystemTrayCoordinator>(tray);
        services.AddSingleton<IApplicationShell>(shell);
        services.AddSingleton<IMainWindowBootstrapper>(bootstrapper);
        services.AddSingleton<ILogger<ApplicationStartupCoordinator>>(
            _ => NullLogger<ApplicationStartupCoordinator>.Instance);
        services.AddSingleton<IApplicationStartupCoordinator>(sp =>
            new ApplicationStartupCoordinator(
                sp,
                sp.GetRequiredService<IApplicationShell>(),
                NullLogger<ApplicationStartupCoordinator>.Instance,
                trayOnlyMode));

        var provider = services.BuildServiceProvider();
        var startup = provider.GetRequiredService<IApplicationStartupCoordinator>();

        return new StartupHarness(
            desktop,
            shell,
            bootstrapper,
            presenter,
            tray,
            startup,
            initializationOrder);
    }

    private sealed class StartupHarness
    {
        public StartupHarness(
            FakeDesktopApplicationHost desktop,
            ApplicationShell shell,
            RecordingMainWindowBootstrapper bootstrapper,
            RecordingMainWindowPresenter presenter,
            RecordingStartupTrayCoordinator tray,
            IApplicationStartupCoordinator startup,
            List<string> initializationOrder)
        {
            Desktop = desktop;
            Shell = shell;
            Bootstrapper = bootstrapper;
            Presenter = presenter;
            Tray = tray;
            Startup = startup;
            InitializationOrder = initializationOrder;
        }

        public FakeDesktopApplicationHost Desktop { get; }

        public ApplicationShell Shell { get; }

        public RecordingMainWindowBootstrapper Bootstrapper { get; }

        public RecordingMainWindowPresenter Presenter { get; }

        public RecordingStartupTrayCoordinator Tray { get; }

        public IApplicationStartupCoordinator Startup { get; }

        public List<string> InitializationOrder { get; }
    }

    private sealed class FakeDesktopApplicationHost : IDesktopApplicationHost
    {
        public Window? MainWindow { get; set; }

        public ShutdownMode ShutdownMode { get; set; } = ShutdownMode.OnMainWindowClose;

        public int ShutdownCallCount { get; private set; }

        public void Shutdown()
        {
            ShutdownCallCount++;
        }
    }

    private sealed class RecordingMainWindowBootstrapper : IMainWindowBootstrapper
    {
        private readonly RecordingMainWindowPresenter _presenter;

        public RecordingMainWindowBootstrapper(RecordingMainWindowPresenter presenter)
        {
            _presenter = presenter;
        }

        public int CreateCallCount { get; private set; }

        public MainWindowBootstrapResult Create()
        {
            CreateCallCount++;
            return new MainWindowBootstrapResult
            {
                Window = new Window(),
                ViewModel = null!,
                Presenter = _presenter,
            };
        }
    }

    private sealed class RecordingMainWindowPresenter : IMainWindowPresenter
    {
        public bool IsVisible { get; private set; }

        public WindowState WindowState { get; set; } = WindowState.Normal;

        public int ShowCallCount { get; private set; }

        public int ActivateCallCount { get; private set; }

        public event EventHandler<MainWindowClosingEventArgs>? Closing;

        public void Hide()
        {
            IsVisible = false;
        }

        public void Show()
        {
            ShowCallCount++;
            IsVisible = true;
        }

        public void Activate()
        {
            ActivateCallCount++;
        }
    }

    private sealed class RecordingStartupTrayCoordinator : ISystemTrayCoordinator
    {
        private readonly bool _isTrayAvailable;
        private readonly List<string>? _initializationOrder;

        public RecordingStartupTrayCoordinator(bool isTrayAvailable, List<string>? initializationOrder = null)
        {
            _isTrayAvailable = isTrayAvailable;
            _initializationOrder = initializationOrder;
        }

        public int InitializeCallCount { get; private set; }

        public bool IsTrayAvailable { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCallCount++;
            IsTrayAvailable = _isTrayAvailable;
            _initializationOrder?.Add(nameof(ISystemTrayCoordinator));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingInputDiagnosticCoordinator : IInputDiagnosticCoordinator
    {
        private readonly List<string> _order;

        public RecordingInputDiagnosticCoordinator(List<string> order)
        {
            _order = order;
        }

        public bool IsMonitoring => false;

        public event EventHandler<InputDiagnosticEvent>? DiagnosticReceived;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(IInputDiagnosticCoordinator));
            return Task.CompletedTask;
        }

        public Task ApplyProtectionStateAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void ClearRecentEvents()
        {
        }

        public IReadOnlyList<InputDiagnosticEvent> GetRecentEvents() => [];
    }

    private sealed class RecordingLayoutCoordinator : ILiveLayoutCorrectionCoordinator
    {
        private readonly List<string> _order;
        private readonly bool _shouldFail;

        public RecordingLayoutCoordinator(List<string> order, bool shouldFail)
        {
            _order = order;
            _shouldFail = shouldFail;
        }

        public LiveLayoutCorrectionStatus Status => new();

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(ILiveLayoutCorrectionCoordinator));
            if (_shouldFail)
            {
                throw new InvalidOperationException("Simulated layout coordinator failure.");
            }

            return Task.CompletedTask;
        }

        public void ResetBuffer()
        {
        }
    }

    private sealed class RecordingCorrectionNotificationCoordinator : ICorrectionNotificationCoordinator, IAsyncDisposable
    {
        private readonly List<string> _order;

        public RecordingCorrectionNotificationCoordinator(List<string> order) => _order = order;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(ICorrectionNotificationCoordinator));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingPredictionCoordinator : ILivePredictionCoordinator
    {
        private readonly List<string> _order;

        public RecordingPredictionCoordinator(List<string> order) => _order = order;

        public LivePredictionStatus Status => new();

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(ILivePredictionCoordinator));
            return Task.CompletedTask;
        }

        public void ResetBuffer()
        {
        }
    }

    private sealed class RecordingOverlayCoordinator : ILivePredictionOverlayCoordinator
    {
        private readonly List<string> _order;

        public RecordingOverlayCoordinator(List<string> order) => _order = order;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(ILivePredictionOverlayCoordinator));
            return Task.CompletedTask;
        }

        public Task SyncOverlayAsync() => Task.CompletedTask;
    }

    private sealed class RecordingTabAcceptanceCoordinator : ILivePredictionTabAcceptanceCoordinator
    {
        private readonly List<string> _order;

        public RecordingTabAcceptanceCoordinator(List<string> order) => _order = order;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(ILivePredictionTabAcceptanceCoordinator));
            return Task.CompletedTask;
        }

        public Task ProcessTabAcceptanceAsync(KeyboardObservationEventArgs observation) => Task.CompletedTask;
    }

    private sealed class RecordingEscDismissalCoordinator : ILivePredictionEscDismissalCoordinator
    {
        private readonly List<string> _order;

        public RecordingEscDismissalCoordinator(List<string> order) => _order = order;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(ILivePredictionEscDismissalCoordinator));
            return Task.CompletedTask;
        }

        public Task ProcessEscDismissalAsync(KeyboardObservationEventArgs observation) => Task.CompletedTask;
    }

    private sealed class RecordingUndoHotkeyCoordinator : IUndoHotkeyCoordinator
    {
        private readonly List<string> _order;

        public RecordingUndoHotkeyCoordinator(List<string> order) => _order = order;

        public GlobalHotkeyRegistrationResult? RegistrationStatus => null;

        public string CurrentBindingDisplayName => string.Empty;

        public event EventHandler? RegistrationChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(IUndoHotkeyCoordinator));
            return Task.CompletedTask;
        }

        public Task<GlobalHotkeyRegistrationResult> ApplyBindingAsync(
            string hotkeyCombination,
            CancellationToken cancellationToken = default)
            => Task.FromResult(GlobalHotkeyRegistrationResult.Registered("undo", hotkeyCombination));

        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingAutomaticLayoutHotkeyCoordinator : IAutomaticLayoutHotkeyCoordinator
    {
        private readonly List<string> _order;

        public RecordingAutomaticLayoutHotkeyCoordinator(List<string> order) => _order = order;

        public GlobalHotkeyRegistrationResult? RegistrationStatus => null;

        public string CurrentBindingDisplayName => string.Empty;

        public event EventHandler? RegistrationChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(IAutomaticLayoutHotkeyCoordinator));
            return Task.CompletedTask;
        }

        public Task<GlobalHotkeyRegistrationResult> ApplyBindingAsync(
            string hotkeyCombination,
            CancellationToken cancellationToken = default)
            => Task.FromResult(GlobalHotkeyRegistrationResult.Registered("toggle-layout", hotkeyCombination));

        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingDoubleShiftUndoCoordinator : IDoubleShiftUndoCoordinator
    {
        private readonly List<string> _order;

        public RecordingDoubleShiftUndoCoordinator(List<string> order) => _order = order;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(IDoubleShiftUndoCoordinator));
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingManualHotkeyCoordinator : IManualCorrectionHotkeyCoordinator
    {
        private readonly List<string> _order;

        public RecordingManualHotkeyCoordinator(List<string> order) => _order = order;

        public IReadOnlyDictionary<string, GlobalHotkeyRegistrationResult?> RegistrationStatuses { get; } =
            new Dictionary<string, GlobalHotkeyRegistrationResult?>();

        public IReadOnlyDictionary<string, string> CurrentBindingDisplayNames { get; } =
            new Dictionary<string, string>();

        public event EventHandler? RegistrationChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _order.Add(nameof(IManualCorrectionHotkeyCoordinator));
            return Task.CompletedTask;
        }

        public Task<GlobalHotkeyRegistrationResult> ApplyBindingAsync(
            string hotkeyId,
            string hotkeyCombination,
            CancellationToken cancellationToken = default)
            => Task.FromResult(GlobalHotkeyRegistrationResult.Registered("undo", hotkeyCombination));

        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingTrayCoordinator : ISystemTrayCoordinator, IAsyncDisposable
    {
        public int DisposeCallCount { get; private set; }

        public bool IsTrayAvailable => true;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }
}
