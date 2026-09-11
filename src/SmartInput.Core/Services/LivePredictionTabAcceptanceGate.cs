using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.Core.Services;

public sealed class LivePredictionTabAcceptanceGate : IPredictionTabAcceptanceGate
{
    private readonly ILivePredictionEngine _predictionEngine;
    private readonly IPredictionOverlayService _overlayService;
    private readonly ISettingsService _settingsService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly IEmergencyPauseService _emergencyPauseService;
    private readonly ITextReplacementSessionNotifier _replacementSessionNotifier;

    public LivePredictionTabAcceptanceGate(
        ILivePredictionEngine predictionEngine,
        IPredictionOverlayService overlayService,
        ISettingsService settingsService,
        IAutomationSafetyService automationSafetyService,
        IEmergencyPauseService emergencyPauseService,
        ITextReplacementSessionNotifier replacementSessionNotifier)
    {
        _predictionEngine = predictionEngine;
        _overlayService = overlayService;
        _settingsService = settingsService;
        _automationSafetyService = automationSafetyService;
        _emergencyPauseService = emergencyPauseService;
        _replacementSessionNotifier = replacementSessionNotifier;
    }

    public bool ShouldInterceptPlainTab()
    {
        if (_replacementSessionNotifier.IsReplacementActive)
        {
            return false;
        }

        if (_emergencyPauseService.IsPaused)
        {
            return false;
        }

        var settings = _settingsService.Current;
        if (!settings.IsEnabled || !settings.PredictionEnabled)
        {
            return false;
        }

        if (_overlayService.Status.Visibility != PredictionOverlayVisibility.Visible)
        {
            return false;
        }

        if (!_predictionEngine.Status.HasSuggestion)
        {
            return false;
        }

        var policy = _automationSafetyService.EvaluateCurrentContext();
        return policy.State == AutomationPolicyState.Allowed
            && policy.AllowsAutomation
            && !policy.IsEmergencyPaused;
    }
}
