using Microsoft.Extensions.DependencyInjection;
using SmartInput.App.Services;
using SmartInput.Core.Diagnostics;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Integration;
using SmartInput.Core.Services;
using SmartInput.Core.Models;
using SmartInput.Infrastructure.DependencyInjection;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Safety;
using SmartInput.Platform.Windows.DependencyInjection;

namespace SmartInput.Runtime.DependencyInjection;

/// <summary>
/// Registers every service needed by the keyboard hook, correction pipeline,
/// native overlays and hotkeys. This composition has no Avalonia dependency so
/// it can live in a small resident process later.
/// </summary>
public static class RuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddSmartInputRuntime(this IServiceCollection services)
    {
        services.AddSmartInputInfrastructure();
        services.AddSmartInputWindowsPlatform();

        services.AddSingleton<IPerformanceDebugLoggingGate, SettingsPerformanceDebugLoggingGate>();
        services.AddSingleton<IPerformanceMetricsRecorder, PerformanceMetricsCollector>();
        services.AddSingleton<ICorrectionOwnershipGate, CorrectionOwnershipGate>();
        // One FIFO for all event consumers that mutate or read live text
        // state. This prevents Double Shift from overtaking Core processing.
        services.AddSingleton<SerializedObservationQueue>();

        services.AddSingleton<ISafeTextReplacementService, SafeTextReplacementService>();
        services.AddSingleton<ICorrectionApplicationContext, CorrectionApplicationContext>();
        services.AddSingleton<ICorrectionRejectionBackspaceService, CorrectionRejectionBackspaceService>();
        services.AddSingleton<ICorrectionUndoService, CorrectionUndoService>();
        services.AddSingleton<ISafetyPolicyEvaluator, SafetyPolicyEvaluator>();
        services.AddSingleton<IAutomationSafetyService, AutomationSafetyService>();
        services.AddSingleton<ILayoutConversionService, KeyboardLayoutConverter>();
        // The early model is immutable after startup. It is trained once from
        // the embedded RU/EN lexicon; Evaluate itself performs no I/O or
        // allocations and is safe to call on the hook preflight path.
        services.AddSingleton<EarlyLayoutModel>(_ => EarlyLayoutModel.Train(
            StarterAutocorrectLexicon.Entries
                .Select(pair => (
                    Language: pair.Key.Language,
                    Word: pair.Key.Word,
                    Weight: pair.Value * pair.Value))));
        services.AddSingleton<EarlyLayoutSession>();
        services.AddSingleton<IEarlyLayoutCorrectionService, EarlyLayoutCorrectionService>();
        services.AddSingleton<IWrongLayoutDetectionService>(sp =>
            new WrongLayoutDetectionService(
                sp.GetRequiredService<ILayoutConversionService>(),
                sp.GetRequiredService<IAutocorrectDictionary>()));
        services.AddSingleton<IAutocorrectionService>(sp => new AutocorrectionService(
            externalEvaluator: sp.GetRequiredService<IExternalAutocorrectionEvaluator>(),
            externalProvider: sp.GetRequiredService<IExternalSpellCorrectionProvider>(),
            settingsService: sp.GetRequiredService<ISettingsService>()));
        services.AddSingleton<IJointCorrectionDecisionService>(sp => new JointCorrectionDecisionService(
            sp.GetRequiredService<IWrongLayoutDetectionService>(),
            sp.GetRequiredService<IAutocorrectionService>(),
            sp.GetRequiredService<ILayoutConversionService>()));
        services.AddSingleton<IPortableCorrectionEngine, PortableCorrectionEngine>();
        // The scorer remains audit-only unless both the native DLL and the
        // explicit allow-list environment switch are present. In live mode it
        // still returns only a candidate; C# owns all safety, replacement and
        // Undo operations.
        services.AddSingleton<AdditionalCorrectionScoringAuditService>();
        services.AddSingleton<IRustShadowAuditService, RustShadowAuditService>();
        services.AddSingleton<IRustHybridCorrectionService>(sp => new RustHybridCorrectionService(
            sp.GetRequiredService<IRustShadowCandidateProvider>(),
            sp.GetRequiredService<ILayoutConversionService>(),
            RustHybridCorrectionService.IsAllowListModeEnabled()));
        services.AddSingleton<IKbmAllowListCorrectionService, KbmAllowListCorrectionService>();
        services.AddSingleton<IPredictionService, PredictionService>();
        services.AddSingleton<ILivePredictionEngine, LivePredictionEngine>();
        services.AddSingleton<IPredictionTabAcceptanceService, PredictionTabAcceptanceService>();
        services.AddSingleton<IPredictionTabAcceptanceGate, LivePredictionTabAcceptanceGate>();
        services.AddSingleton<IPredictionEscDismissalService, PredictionEscDismissalService>();
        services.AddSingleton<IPredictionEscDismissalGate, LivePredictionEscDismissalGate>();
        services.AddSingleton<IPunctuationCorrectionService, PunctuationCorrectionService>();
        services.AddSingleton<IPunctuationProvider, RuleBasedPunctuationProvider>();
        services.AddSingleton<IPunctuationPreviewService, PunctuationPreviewService>();
        services.AddSingleton<RecentTextContextBuffer>();
        services.AddSingleton<SentenceLanguageContextBuffer>();
        services.AddSingleton<IAutomaticLayoutCorrectionEngine>(sp => new AutomaticLayoutCorrectionEngine(
            sp.GetRequiredService<IWrongLayoutDetectionService>(),
            sp.GetRequiredService<IAutocorrectionService>(),
            sp.GetRequiredService<IAutocorrectDictionary>(),
            sp.GetRequiredService<ISnippetService>(),
            sp.GetRequiredService<ISafeTextReplacementService>(),
            sp.GetRequiredService<IAutomationSafetyService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IBoundaryKeyDeliveryService>(),
            sp.GetRequiredService<ICorrectionUndoService>(),
            sp.GetRequiredService<ICorrectionApplicationContext>(),
            sp.GetRequiredService<IPerformanceMetricsRecorder>(),
            rejectionPolicy: sp.GetRequiredService<ICorrectionRejectionPolicy>(),
            feedbackNotifier: sp.GetRequiredService<ICorrectionFeedbackNotifier>(),
            jointCorrectionDecisionService: sp.GetRequiredService<IJointCorrectionDecisionService>(),
            keyboardInputLanguageService: sp.GetService<IKeyboardInputLanguageService>(),
            punctuationCorrectionService: sp.GetRequiredService<IPunctuationCorrectionService>(),
            recentTextContext: sp.GetRequiredService<RecentTextContextBuffer>(),
            kbmAllowList: sp.GetRequiredService<IKbmAllowListCorrectionService>(),
            rustShadowAudit: sp.GetRequiredService<IRustShadowAuditService>(),
            rustHybrid: sp.GetRequiredService<IRustHybridCorrectionService>(),
            earlyLayoutCorrectionService: sp.GetRequiredService<IEarlyLayoutCorrectionService>(),
            sentenceLanguageContext: sp.GetRequiredService<SentenceLanguageContextBuffer>()));
        services.AddSingleton<ILiveLayoutBoundaryGate>(sp => new LiveLayoutBoundaryGate(
            sp.GetRequiredService<IAutomaticLayoutCorrectionEngine>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IEmergencyPauseService>(),
            sp.GetRequiredService<ITextReplacementSessionNotifier>(),
            sp.GetRequiredService<IWrongLayoutDetectionService>(),
            sp.GetRequiredService<IAutocorrectDictionary>(),
            sp.GetRequiredService<IEarlyLayoutCorrectionService>(),
            sp.GetRequiredService<SentenceLanguageContextBuffer>()));
        services.AddSingleton<CorrectionNotificationCoordinator>();
        services.AddSingleton<ICorrectionFeedbackNotifier>(sp =>
            sp.GetRequiredService<CorrectionNotificationCoordinator>());
        services.AddSingleton<ICorrectionNotificationCoordinator>(sp =>
            sp.GetRequiredService<CorrectionNotificationCoordinator>());
        services.AddSingleton<IManualLayoutConversionService, ManualLayoutConversionService>();
        services.AddSingleton<IManualSelectedTextCorrectionService, ManualSelectedTextCorrectionService>();
        services.AddSingleton<IInputDiagnosticCoordinator, InputDiagnosticCoordinator>();
        services.AddSingleton<ILiveLayoutCorrectionCoordinator, LiveLayoutCorrectionCoordinator>();
        services.AddSingleton<ILivePredictionCoordinator, LivePredictionCoordinator>();
        services.AddSingleton<ILivePredictionOverlayCoordinator, LivePredictionOverlayCoordinator>();
        services.AddSingleton<ILivePredictionTabAcceptanceCoordinator, LivePredictionTabAcceptanceCoordinator>();
        services.AddSingleton<ILivePredictionEscDismissalCoordinator, LivePredictionEscDismissalCoordinator>();
        services.AddSingleton<IAutomaticLayoutHotkeyCoordinator, AutomaticLayoutHotkeyCoordinator>();
        services.AddSingleton<IUndoHotkeyCoordinator, UndoHotkeyCoordinator>();
        services.AddSingleton<IDoubleShiftUndoCoordinator>(sp => new DoubleShiftUndoCoordinator(
            sp.GetRequiredService<IInputMonitor>(),
            sp.GetRequiredService<ICorrectionUndoService>(),
            sp.GetRequiredService<IPerformanceMetricsRecorder>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DoubleShiftUndoCoordinator>>(),
            sp.GetRequiredService<ISafeTextReplacementService>(),
            sp.GetRequiredService<RecentTextContextBuffer>(),
            sp.GetRequiredService<ILayoutConversionService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IKeyboardModifierState>(),
            sp.GetRequiredService<SerializedObservationQueue>()));
        services.AddSingleton<IManualCorrectionHotkeyCoordinator, ManualCorrectionHotkeyCoordinator>();

        return services;
    }
}
