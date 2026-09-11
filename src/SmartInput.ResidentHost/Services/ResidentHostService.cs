using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartInput.App.Services;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Services;

namespace SmartInput.ResidentHost.Services;

/// <summary>
/// Starts the keyboard runtime without creating an Avalonia application.
/// The native Windows hook, overlay and tray each own the message loop they
/// require, so a settings window is not part of this resident process.
/// </summary>
internal sealed class ResidentHostService : IHostedService
{
    private readonly IInputDiagnosticCoordinator _diagnostics;
    private readonly ILiveLayoutCorrectionCoordinator _layout;
    private readonly ICorrectionNotificationCoordinator _notifications;
    private readonly IAutomaticLayoutHotkeyCoordinator _automaticLayoutHotkey;
    private readonly IUndoHotkeyCoordinator _undoHotkey;
    private readonly IDoubleShiftUndoCoordinator _doubleShiftUndo;
    private readonly IManualCorrectionHotkeyCoordinator _manualCorrectionHotkey;
    private readonly ResidentSettingsReloadCoordinator _settingsReload;
    private readonly ResidentSnippetReloadCoordinator _snippetReload;
    private readonly ResidentLearningReloadCoordinator _learningReload;
    private readonly ResidentUserDictionaryReloadCoordinator _userDictionaryReload;
    private readonly ResidentPredictionFeatureCoordinator _predictionFeatures;
    private readonly ResidentTrayCoordinator _tray;
    private readonly IServiceProvider _services;
    private readonly ILogger<ResidentHostService> _logger;

    public ResidentHostService(
        IInputDiagnosticCoordinator diagnostics,
        ILiveLayoutCorrectionCoordinator layout,
        ICorrectionNotificationCoordinator notifications,
        IAutomaticLayoutHotkeyCoordinator automaticLayoutHotkey,
        IUndoHotkeyCoordinator undoHotkey,
        IDoubleShiftUndoCoordinator doubleShiftUndo,
        IManualCorrectionHotkeyCoordinator manualCorrectionHotkey,
        ResidentSettingsReloadCoordinator settingsReload,
        ResidentSnippetReloadCoordinator snippetReload,
        ResidentLearningReloadCoordinator learningReload,
        ResidentUserDictionaryReloadCoordinator userDictionaryReload,
        ResidentPredictionFeatureCoordinator predictionFeatures,
        ResidentTrayCoordinator tray,
        IServiceProvider services,
        ILogger<ResidentHostService> logger)
    {
        _diagnostics = diagnostics;
        _layout = layout;
        _notifications = notifications;
        _automaticLayoutHotkey = automaticLayoutHotkey;
        _undoHotkey = undoHotkey;
        _doubleShiftUndo = doubleShiftUndo;
        _manualCorrectionHotkey = manualCorrectionHotkey;
        _settingsReload = settingsReload;
        _snippetReload = snippetReload;
        _learningReload = learningReload;
        _userDictionaryReload = userDictionaryReload;
        _predictionFeatures = predictionFeatures;
        _tray = tray;
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Load settings, warm the bounded dictionaries and initialize the live
        // coordinator before enabling WH_KEYBOARD_LL. No lazy dictionary I/O
        // or first-policy evaluation is allowed inside the low-level hook.
        await _services.GetRequiredService<ISettingsService>()
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        await _services.GetRequiredService<ISnippetService>()
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        _services.GetService<IHunspellWordFormProvider>()?.WarmUp();
        if (_services.GetRequiredService<ISettingsService>().Current.ExternalSpellingEngineEnabled)
        {
            _services.GetService<SymSpellSpellCorrectionProvider>()?.WarmUp();
        }
        await _layout.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _diagnostics.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _notifications.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _predictionFeatures.StartAsync(cancellationToken).ConfigureAwait(false);
        await _automaticLayoutHotkey.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _undoHotkey.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _doubleShiftUndo.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _manualCorrectionHotkey.InitializeAsync(cancellationToken).ConfigureAwait(false);

        _settingsReload.Start();
        _snippetReload.Start();
        _learningReload.Start();
        _userDictionaryReload.Start();
        await _tray.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Resident keyboard runtime initialized.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _settingsReload.DisposeAsync().ConfigureAwait(false);
        await _snippetReload.DisposeAsync().ConfigureAwait(false);
        await _learningReload.DisposeAsync().ConfigureAwait(false);
        await _userDictionaryReload.DisposeAsync().ConfigureAwait(false);
        await _tray.DisposeAsync().ConfigureAwait(false);
        await _predictionFeatures.DisposeAsync().ConfigureAwait(false);

        await DisposeAsyncCoordinator<IManualCorrectionHotkeyCoordinator>().ConfigureAwait(false);
        await DisposeAsyncCoordinator<IDoubleShiftUndoCoordinator>().ConfigureAwait(false);
        await DisposeAsyncCoordinator<IUndoHotkeyCoordinator>().ConfigureAwait(false);
        await DisposeAsyncCoordinator<IAutomaticLayoutHotkeyCoordinator>().ConfigureAwait(false);
        await DisposeAsyncCoordinator<ICorrectionNotificationCoordinator>().ConfigureAwait(false);

        if (_services.GetService<IInputDiagnosticCoordinator>() is IDisposable diagnostics)
        {
            diagnostics.Dispose();
        }

        if (_services.GetService<ILiveLayoutCorrectionCoordinator>() is IDisposable layout)
        {
            layout.Dispose();
        }

    }

    private async Task DisposeAsyncCoordinator<T>() where T : class
    {
        if (_services.GetService<T>() is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}
