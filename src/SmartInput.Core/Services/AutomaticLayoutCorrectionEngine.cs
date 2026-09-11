using System.Diagnostics;
using SmartInput.Core.Configuration;
using SmartInput.Core.Diagnostics;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Integration;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Services;

public interface IAutomaticLayoutCorrectionEngine
{
    LiveLayoutCorrectionStatus Status { get; }

    int PendingTokenLength { get; }

    int PendingSnippetTriggerLength { get; }

    bool HasPendingLiveWork { get; }

    AutomationPolicyState CachedPolicyState { get; }

    /// <summary>
    /// True when punctuation spacing correction would apply for the deferred boundary.
    /// Used by the boundary gate for safe suppression/reinjection ordering.
    /// </summary>
    bool CanApplyPunctuationCorrection(DeferredBoundaryKey boundary);

    Task ProcessInputAsync(TokenInputEvent input, CancellationToken cancellationToken = default);

    void ResetBuffer(string reason);

    void NotifyApplicationContextChanged();

    void NotifyPolicyContextChanged(AutomationPolicyResult policy);
}

public sealed class AutomaticLayoutCorrectionEngine : IAutomaticLayoutCorrectionEngine
{
    private readonly CurrentTokenBuffer _tokenBuffer;
    private readonly SnippetTriggerBuffer _snippetTriggerBuffer;
    private readonly IWrongLayoutDetectionService _wrongLayoutDetectionService;
    private readonly IAutocorrectionService _autocorrectionService;
    private readonly IJointCorrectionDecisionService _jointCorrectionDecisionService;
    private readonly IAutocorrectDictionary _autocorrectDictionary;
    private readonly ISnippetService _snippetService;
    private readonly ISafeTextReplacementService _safeTextReplacementService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly ISettingsService _settingsService;
    private readonly IBoundaryKeyDeliveryService _boundaryKeyDeliveryService;
    private readonly ICorrectionUndoService _correctionUndoService;
    private readonly ICorrectionApplicationContext _correctionApplicationContext;
    private readonly IPerformanceMetricsRecorder _performanceMetrics;
    private readonly ICorrectionRejectionPolicy _rejectionPolicy;
    private readonly ICorrectionFeedbackNotifier _feedbackNotifier;
    private readonly IPunctuationCorrectionService _punctuationCorrectionService;
    private readonly RecentTextContextBuffer _recentTextContext;
    private readonly IKbmAllowListCorrectionService? _kbmAllowList;
    private readonly IRustHybridCorrectionService? _rustHybrid;
    private readonly IRustShadowAuditService? _rustShadowAudit;
    private readonly IEarlyLayoutCorrectionService? _earlyLayoutCorrectionService;
    private readonly IKeyboardInputLanguageService? _keyboardInputLanguageService;
    private readonly LayoutCorrectionOptions _options;
    private readonly AutocorrectionOptions _autocorrectionOptions;
    private readonly SentenceLanguageContextBuffer _sentenceLanguageContext;
    private readonly object _statusSync = new();

    private int _tokensCompleted;
    private int _candidatesDetected;
    private int _correctionsAttempted;
    private int _correctionsSucceeded;
    private int _correctionsBlocked;
    private int _autocorrectCandidatesDetected;
    private int _autocorrectAttempts;
    private int _autocorrectSucceeded;
    private int _autocorrectBlocked;
    private int _snippetExpansionsAttempted;
    private int _snippetExpansionsSucceeded;
    private int _snippetExpansionsBlocked;
    private int _punctuationAttempts;
    private int _punctuationSucceeded;
    private int _punctuationBlocked;
    private int _bufferResets;
    private int _boundariesSuppressed;
    private int _boundariesDelivered;
    private AutomationPolicyState _lastPolicyState = AutomationPolicyState.UnknownContext;
    private LiveLayoutCorrectionAction _lastAction = LiveLayoutCorrectionAction.None;

    public AutomaticLayoutCorrectionEngine(
        IWrongLayoutDetectionService wrongLayoutDetectionService,
        IAutocorrectionService autocorrectionService,
        IAutocorrectDictionary autocorrectDictionary,
        ISnippetService snippetService,
        ISafeTextReplacementService safeTextReplacementService,
        IAutomationSafetyService automationSafetyService,
        ISettingsService settingsService,
        IBoundaryKeyDeliveryService boundaryKeyDeliveryService,
        ICorrectionUndoService correctionUndoService,
        ICorrectionApplicationContext correctionApplicationContext,
        IPerformanceMetricsRecorder? performanceMetrics = null,
        LayoutCorrectionOptions? options = null,
        AutocorrectionOptions? autocorrectionOptions = null,
        CurrentTokenBufferOptions? bufferOptions = null,
        SnippetTriggerBufferOptions? snippetBufferOptions = null,
        IKeyboardInputLanguageService? keyboardInputLanguageService = null,
        ICorrectionRejectionPolicy? rejectionPolicy = null,
        ICorrectionFeedbackNotifier? feedbackNotifier = null,
        IJointCorrectionDecisionService? jointCorrectionDecisionService = null,
        SentenceLanguageContextBuffer? sentenceLanguageContext = null,
        IPunctuationCorrectionService? punctuationCorrectionService = null,
        RecentTextContextBuffer? recentTextContext = null,
        IKbmAllowListCorrectionService? kbmAllowList = null,
        IRustShadowAuditService? rustShadowAudit = null,
        IRustHybridCorrectionService? rustHybrid = null,
        IEarlyLayoutCorrectionService? earlyLayoutCorrectionService = null)
    {
        _wrongLayoutDetectionService = wrongLayoutDetectionService;
        _autocorrectionService = autocorrectionService;
        _jointCorrectionDecisionService = jointCorrectionDecisionService
            ?? new JointCorrectionDecisionService(
                wrongLayoutDetectionService,
                autocorrectionService,
                new KeyboardLayoutConverter());
        _autocorrectDictionary = autocorrectDictionary;
        _snippetService = snippetService;
        _safeTextReplacementService = safeTextReplacementService;
        _automationSafetyService = automationSafetyService;
        _settingsService = settingsService;
        _boundaryKeyDeliveryService = boundaryKeyDeliveryService;
        _correctionUndoService = correctionUndoService;
        _correctionApplicationContext = correctionApplicationContext;
        _performanceMetrics = performanceMetrics ?? NullPerformanceMetricsRecorder.Instance;
        _rejectionPolicy = rejectionPolicy ?? NullCorrectionRejectionPolicy.Instance;
        _feedbackNotifier = feedbackNotifier ?? NullCorrectionFeedbackNotifier.Instance;
        _punctuationCorrectionService = punctuationCorrectionService ?? new PunctuationCorrectionService();
        _recentTextContext = recentTextContext ?? new RecentTextContextBuffer();
        _kbmAllowList = kbmAllowList;
        _rustHybrid = rustHybrid;
        _rustShadowAudit = rustShadowAudit;
        _earlyLayoutCorrectionService = earlyLayoutCorrectionService;
        _options = options ?? new LayoutCorrectionOptions();
        _autocorrectionOptions = autocorrectionOptions ?? new AutocorrectionOptions();
        _keyboardInputLanguageService = keyboardInputLanguageService;
        _tokenBuffer = new CurrentTokenBuffer(bufferOptions);
        _snippetTriggerBuffer = new SnippetTriggerBuffer(snippetBufferOptions);
        _sentenceLanguageContext = sentenceLanguageContext ?? new SentenceLanguageContextBuffer();
    }

    public int PendingTokenLength => _tokenBuffer.Length;

    public int PendingSnippetTriggerLength => _snippetTriggerBuffer.Length;

    public bool HasPendingLiveWork =>
        !_tokenBuffer.IsEmpty
        || !_snippetTriggerBuffer.IsEmpty
        || _recentTextContext.Length > 0;

    public bool CanApplyPunctuationCorrection(DeferredBoundaryKey boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);

        if (!IsProtectionEnabled() || !_settingsService.Current.PunctuationEnabled)
        {
            return false;
        }

        var policy = CachedPolicyState == AutomationPolicyState.Allowed
            ? CreateCachedAllowedPolicy()
            : _automationSafetyService.EvaluateCurrentContext();
        if (!IsPolicyAllowedForLiveCorrection(policy))
        {
            return false;
        }

        if (!TryGetPunctuationCharacter(boundary, out var punctuationCharacter))
        {
            return false;
        }

        return _punctuationCorrectionService
            .Evaluate(_recentTextContext.Snapshot(), punctuationCharacter)
            .Recommendation == PunctuationCorrectionRecommendation.Apply;
    }

    public AutomationPolicyState CachedPolicyState
    {
        get
        {
            lock (_statusSync)
            {
                return _lastPolicyState;
            }
        }
    }

    public LiveLayoutCorrectionStatus Status
    {
        get
        {
            lock (_statusSync)
            {
                return BuildStatus();
            }
        }
    }

    public async Task ProcessInputAsync(TokenInputEvent input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.PreparedLayoutCorrection is not null
            && input.Kind == TokenInputKind.WordBoundary
            && !input.IsEarlyLayoutCorrection)
        {
            await ProcessPreparedLayoutCorrectionAsync(
                    input.PreparedLayoutCorrection,
                    input.DeferredBoundary,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (input.DeferredBoundary is not null)
        {
            IncrementBoundariesSuppressed();
        }

        if (input.Kind is TokenInputKind.Reset or TokenInputKind.Uncertain)
        {
            _earlyLayoutCorrectionService?.Reset();
            ResetBufferInternal(input.Kind == TokenInputKind.Uncertain
                ? LiveLayoutCorrectionAction.TokenDiscarded
                : LiveLayoutCorrectionAction.BufferReset);
            return;
        }

        // Fail open. If the target application already received the boundary,
        // a "recent token" replacement could delete that boundary instead.
        // Missing a correction is always safer than corrupting normal typing.
        if (input.Kind == TokenInputKind.WordBoundary
            && !input.AllowsTextReplacementAtBoundary)
        {
            // Keep the just-finished token available for an explicit Double
            // Shift fallback even though the boundary itself is delivered
            // fail-open and the live buffers are reset.
            _recentTextContext.RememberCompletedToken(ExtractLastToken(_recentTextContext.Snapshot()));
            ResetBufferInternal(
                LiveLayoutCorrectionAction.CorrectionSkippedUnsafeBoundary,
                preserveLastCompletedToken: true);
            return;
        }

        var tokenApply = _tokenBuffer.Apply(input);
        var snippetApply = _snippetTriggerBuffer.Apply(input);
        _recentTextContext.Apply(input);

        if (input.Kind == TokenInputKind.Backspace)
        {
            _earlyLayoutCorrectionService?.Backspace();
        }

        if (input.Kind == TokenInputKind.Character
            && input.IsEarlyLayoutCorrection
            && input.PreparedLayoutCorrection is not null)
        {
            await ProcessEarlyLayoutCorrectionAsync(
                    input.PreparedLayoutCorrection,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (tokenApply.BufferReset
            && tokenApply.ResetReason is not TokenBufferResetReason.NonTokenCharacter
            && input.Kind is not TokenInputKind.WordBoundary)
        {
            IncrementBufferResets();
            SetLastAction(LiveLayoutCorrectionAction.BufferReset);
        }
        else if (snippetApply.BufferReset
            && snippetApply.ResetReason is not TokenBufferResetReason.NonTokenCharacter
            && input.Kind is not TokenInputKind.WordBoundary)
        {
            IncrementBufferResets();
            SetLastAction(LiveLayoutCorrectionAction.BufferReset);
        }

        if (input.Kind != TokenInputKind.WordBoundary)
        {
            return;
        }

        var snippetCompleted = snippetApply.TokenCompleted;
        var tokenCompleted = tokenApply.TokenCompleted;

        if (!snippetCompleted
            && !tokenCompleted
            && string.IsNullOrEmpty(input.CompletedToken))
        {
            try
            {
                await TryPunctuationCorrectionAsync(input.DeferredBoundary, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                // Even a failed punctuation/provider path must not consume a
                // physical boundary. The hook already removed it from the
                // target stream, so the safe fallback is exactly-once delivery.
                await DeliverDeferredBoundaryAsync(input.DeferredBoundary, cancellationToken)
                    .ConfigureAwait(false);
            }
            return;
        }

        IncrementTokensCompleted();
        SetLastAction(LiveLayoutCorrectionAction.TokenCompleted);

        var snippetTrigger = snippetCompleted
            ? _snippetTriggerBuffer.TakeCompletedTrigger()
            : string.Empty;
        var token = !string.IsNullOrEmpty(input.CompletedToken)
            ? input.CompletedToken
            : tokenCompleted
                ? _tokenBuffer.TakeCompletedToken()
                : string.Empty;

        try
        {
            await TryPunctuationCorrectionAsync(input.DeferredBoundary, cancellationToken)
                .ConfigureAwait(false);

            await TryProcessBoundaryAsync(
                    snippetTrigger,
                    token,
                    input.DeferredBoundary,
                    cancellationToken)
                .ConfigureAwait(false);

            if (_earlyLayoutCorrectionService?.TryBuildUndoOriginal(
                    token,
                    _correctionApplicationContext.WindowHandle,
                    out var earlyOriginal) == true
                && !_correctionUndoService.IsUndoAvailable)
            {
                // Keep early Undo alive while the user finishes the word;
                // record the complete transaction only at its boundary.
                RecordUndoTransaction(
                    earlyOriginal,
                    token,
                    CorrectionKind.Layout,
                    input.DeferredBoundary);
            }
        }
        finally
        {
            _tokenBuffer.ClearBuffer();
            _snippetTriggerBuffer.ClearBuffer();
            _earlyLayoutCorrectionService?.Reset();
            await DeliverDeferredBoundaryAsync(input.DeferredBoundary, cancellationToken).ConfigureAwait(false);
        }
    }

    public void ResetBuffer(string reason)
    {
        _ = reason;
        ResetBufferInternal(LiveLayoutCorrectionAction.BufferReset);
    }

    public void NotifyApplicationContextChanged()
    {
        _earlyLayoutCorrectionService?.Reset();
        ResetBufferInternal(LiveLayoutCorrectionAction.BufferReset);
    }

    public void NotifyPolicyContextChanged(AutomationPolicyResult policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        AutomationPolicyState previousState;
        lock (_statusSync)
        {
            previousState = _lastPolicyState;
            _lastPolicyState = policy.State;
        }

        if (previousState != policy.State)
        {
            if (!IsPolicyAllowedForLiveCorrection(policy))
            {
                ResetBufferInternal(LiveLayoutCorrectionAction.BufferReset);
            }
            else
            {
                _tokenBuffer.ClearBuffer();
                _snippetTriggerBuffer.ClearBuffer();
            }
        }

        if (!IsPolicyAllowedForLiveCorrection(policy))
        {
            _earlyLayoutCorrectionService?.Reset();
        }

        if (!IsPolicyAllowedForLiveCorrection(policy))
        {
            _sentenceLanguageContext.SetCollectionEnabled(false);
        }
        else
        {
            _sentenceLanguageContext.SetCollectionEnabled(true);
        }
    }

    private async Task TryProcessBoundaryAsync(
        string snippetTrigger,
        string token,
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        if (!IsProtectionEnabled())
        {
            IncrementCorrectionsBlocked();
            IncrementAutocorrectBlocked();
            IncrementSnippetBlocked();
            IncrementPunctuationBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedDisabled);
            return;
        }

        var snippetsEnabled = _settingsService.Current.SnippetsEnabled;
        var layoutEnabled = _settingsService.Current.AutomaticLayoutEnabled;
        var autocorrectEnabled = _settingsService.Current.AutocorrectEnabled;
        var punctuationEnabled = _settingsService.Current.PunctuationEnabled;

        if (!snippetsEnabled && !layoutEnabled && !autocorrectEnabled && !punctuationEnabled)
        {
            IncrementCorrectionsBlocked();
            IncrementAutocorrectBlocked();
            IncrementSnippetBlocked();
            IncrementPunctuationBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedDisabled);
            return;
        }

        var policy = await GetCurrentPolicyAsync(cancellationToken).ConfigureAwait(false);

        NotifyPolicyContextChanged(policy);

        if (!IsPolicyAllowedForLiveCorrection(policy))
        {
            IncrementCorrectionsBlocked();
            IncrementAutocorrectBlocked();
            IncrementSnippetBlocked();
            IncrementPunctuationBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedPolicy);
            return;
        }

        if (snippetsEnabled && !string.IsNullOrEmpty(snippetTrigger))
        {
            var snippetOutcome = await TrySnippetExpansionAsync(snippetTrigger, boundary, cancellationToken)
                .ConfigureAwait(false);
            if (snippetOutcome == LiveCorrectionAttemptOutcome.Attempted)
            {
                return;
            }
        }
        else if (!string.IsNullOrEmpty(snippetTrigger) && !snippetsEnabled)
        {
            IncrementSnippetBlocked();
            SetLastAction(LiveLayoutCorrectionAction.SnippetSkippedDisabled);
        }

        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        if (layoutEnabled || autocorrectEnabled)
        {
            await TryJointCorrectionAsync(
                    token,
                    boundary,
                    layoutEnabled,
                    autocorrectEnabled,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            IncrementCorrectionsBlocked();
            IncrementAutocorrectBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedDisabled);
        }
    }

    private async Task TryJointCorrectionAsync(
        string token,
        DeferredBoundaryKey? boundary,
        bool layoutEnabled,
        bool autocorrectEnabled,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await TryJointCorrectionCoreAsync(
                    token,
                    boundary,
                    layoutEnabled,
                    autocorrectEnabled,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _performanceMetrics.RecordDuration(
                PerformanceMetricKind.AutocorrectEvaluation,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            _performanceMetrics.RecordDuration(
                PerformanceMetricKind.LayoutEvaluation,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private async Task TryJointCorrectionCoreAsync(
        string token,
        DeferredBoundaryKey? boundary,
        bool layoutEnabled,
        bool autocorrectEnabled,
        CancellationToken cancellationToken)
    {
        var decision = _jointCorrectionDecisionService.Evaluate(
            token,
            _autocorrectDictionary,
            layoutEnabled,
            autocorrectEnabled,
            _autocorrectionOptions,
            languageHint: _sentenceLanguageContext.GetHint());

        // In the default mode Rust is shadow-only. Once the explicit
        // allow-list mode is enabled, the hybrid provider is evaluated once
        // below; running shadow and live evaluation together would double the
        // native call on the keyboard path and add avoidable latency.
        if (_rustHybrid?.IsLiveEnabled != true)
        {
            _rustShadowAudit?.Observe(
                token,
                RustShadowContextFormatter.FromHint(_sentenceLanguageContext.GetHint()),
                decision);
        }

        if (decision.Recommendation != JointCorrectionRecommendation.Apply
            || string.IsNullOrEmpty(decision.ReplacementToken))
        {
            JointCorrectionDecisionResult? rustDecision = null;
            if (_rustHybrid?.IsLiveEnabled == true)
            {
                try
                {
                    rustDecision = _rustHybrid.TryCreateApprovedDecision(
                        token,
                        decision,
                        _autocorrectDictionary,
                        layoutEnabled,
                        autocorrectEnabled,
                        _autocorrectionOptions,
                        _sentenceLanguageContext.GetHint());
                }
                catch (Exception)
                {
                    // Optional native providers must never interrupt the
                    // user's input path. Core continues with its own result.
                    rustDecision = null;
                }
            }

            if (rustDecision is not null)
            {
                decision = rustDecision;
            }
        }

        if (decision.Recommendation != JointCorrectionRecommendation.Apply
            || string.IsNullOrEmpty(decision.ReplacementToken))
        {
            // The recovered KBM model is allowed to participate only through
            // a tiny, independently verified allow-list. The normal SmartInput
            // decision remains authoritative whenever it has a candidate.
            // This path still uses the same rejection policy, replacement
            // service and Double Shift Undo transaction as every other live
            // correction.
            if (autocorrectEnabled
                && _kbmAllowList?.TryGetApprovedCandidate(token, out var kbmCandidate) == true
                && kbmCandidate is not null)
            {
                await TryApplyKbmAllowListCandidateAsync(
                        token,
                        kbmCandidate.Text,
                        boundary,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            RecordJointCorrectionSkipped(
                token,
                decision.Recommendation,
                layoutEnabled,
                autocorrectEnabled);
            RecordSentenceContextToken(token);
            return;
        }

        if (decision.Kind is CorrectionKind.Layout or CorrectionKind.Combined)
        {
            IncrementCandidatesDetected();
            IncrementCorrectionsAttempted();
        }

        if (decision.Kind is CorrectionKind.Autocorrect or CorrectionKind.Combined)
        {
            IncrementAutocorrectCandidatesDetected();
            IncrementAutocorrectAttempts();
        }

        SetLastAction(decision.Kind switch
        {
            CorrectionKind.Autocorrect => LiveLayoutCorrectionAction.AutocorrectAttempted,
            CorrectionKind.Combined => LiveLayoutCorrectionAction.CorrectionAttempted,
            _ => LiveLayoutCorrectionAction.CorrectionAttempted,
        });

        if (_rejectionPolicy.IsAutomaticallySuppressed(
                token,
                decision.ReplacementToken,
                decision.Kind))
        {
            IncrementCorrectionsBlocked();
            IncrementAutocorrectBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedLearning);
            return;
        }

        var replacement = await ExecuteReplacementAsync(
            token,
            decision.ReplacementToken,
            cancellationToken).ConfigureAwait(false);

        if (replacement.Status != TextReplacementStatus.Success)
        {
            SetLastAction(decision.Kind switch
            {
                CorrectionKind.Autocorrect => LiveLayoutCorrectionAction.AutocorrectFailed,
                _ => LiveLayoutCorrectionAction.CorrectionFailed,
            });
            return;
        }

        if (decision.Kind is CorrectionKind.Layout or CorrectionKind.Combined)
        {
            IncrementCorrectionsSucceeded();
        }

        if (decision.Kind is CorrectionKind.Autocorrect or CorrectionKind.Combined)
        {
            IncrementAutocorrectSucceeded();
        }

        SetLastAction(decision.Kind switch
        {
            CorrectionKind.Autocorrect => LiveLayoutCorrectionAction.AutocorrectSucceeded,
            CorrectionKind.Combined => LiveLayoutCorrectionAction.CorrectionSucceeded,
            _ => LiveLayoutCorrectionAction.CorrectionSucceeded,
        });

        RecordUndoTransaction(token, decision.ReplacementToken, decision.Kind, boundary);
        _feedbackNotifier.NotifySuccessfulCorrection(
            decision.Kind == CorrectionKind.Combined ? CorrectionKind.Autocorrect : decision.Kind);

        if (decision.TargetInputLanguage is KeyboardInputLanguage targetLanguage)
        {
            await UpdateForegroundInputLanguageAsync(targetLanguage, cancellationToken).ConfigureAwait(false);
        }

        RecordSentenceContextToken(decision.ReplacementToken);
    }

    private async Task TryApplyKbmAllowListCandidateAsync(
        string token,
        string replacementToken,
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        IncrementAutocorrectCandidatesDetected();
        IncrementAutocorrectAttempts();
        SetLastAction(LiveLayoutCorrectionAction.AutocorrectAttempted);

        if (_rejectionPolicy.IsAutomaticallySuppressed(
                token,
                replacementToken,
                CorrectionKind.Autocorrect))
        {
            IncrementAutocorrectBlocked();
            SetLastAction(LiveLayoutCorrectionAction.AutocorrectSkippedLearning);
            return;
        }

        var replacement = await ExecuteReplacementAsync(
                token,
                replacementToken,
                cancellationToken)
            .ConfigureAwait(false);

        if (replacement.Status != TextReplacementStatus.Success)
        {
            SetLastAction(LiveLayoutCorrectionAction.AutocorrectFailed);
            return;
        }

        IncrementAutocorrectSucceeded();
        SetLastAction(LiveLayoutCorrectionAction.AutocorrectSucceeded);
        RecordUndoTransaction(token, replacementToken, CorrectionKind.Autocorrect, boundary);
        _feedbackNotifier.NotifySuccessfulCorrection(CorrectionKind.Autocorrect);
        RecordSentenceContextToken(replacementToken);
    }

    private async Task TryPunctuationCorrectionAsync(
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        if (boundary is null
            || !IsProtectionEnabled()
            || !_settingsService.Current.PunctuationEnabled)
        {
            return;
        }

        if (!TryGetPunctuationCharacter(boundary, out var punctuationCharacter))
        {
            return;
        }

        var evaluation = _punctuationCorrectionService.Evaluate(
            _recentTextContext.Snapshot(),
            punctuationCharacter);
        if (evaluation.Recommendation != PunctuationCorrectionRecommendation.Apply)
        {
            return;
        }

        var policy = await GetCurrentPolicyAsync(cancellationToken).ConfigureAwait(false);
        NotifyPolicyContextChanged(policy);

        if (!IsPolicyAllowedForLiveCorrection(policy))
        {
            IncrementPunctuationBlocked();
            SetLastAction(LiveLayoutCorrectionAction.PunctuationSkippedPolicy);
            return;
        }

        if (_rejectionPolicy.IsAutomaticallySuppressed(
                evaluation.OriginalSegment,
                evaluation.ReplacementSegment,
                CorrectionKind.Punctuation))
        {
            IncrementPunctuationBlocked();
            SetLastAction(LiveLayoutCorrectionAction.PunctuationSkippedLearning);
            return;
        }

        IncrementPunctuationAttempts();
        SetLastAction(LiveLayoutCorrectionAction.PunctuationAttempted);

        var replacement = await ExecuteReplacementAsync(
            evaluation.OriginalSegment,
            evaluation.ReplacementSegment,
            cancellationToken).ConfigureAwait(false);

        if (replacement.Status != TextReplacementStatus.Success)
        {
            SetLastAction(LiveLayoutCorrectionAction.PunctuationFailed);
            return;
        }

        TrimTrailingSpaceFromRecentContext();
        IncrementPunctuationSucceeded();
        SetLastAction(LiveLayoutCorrectionAction.PunctuationSucceeded);
        RecordUndoTransaction(
            evaluation.OriginalSegment,
            evaluation.ReplacementSegment,
            CorrectionKind.Punctuation,
            boundary);
        _feedbackNotifier.NotifySuccessfulCorrection(CorrectionKind.Punctuation);
    }

    private void TrimTrailingSpaceFromRecentContext()
    {
        if (_recentTextContext.Snapshot().EndsWith(" ", StringComparison.Ordinal))
        {
            _recentTextContext.Apply(TokenInputEvent.Backspace);
        }
    }

    private static bool TryGetPunctuationCharacter(DeferredBoundaryKey boundary, out char punctuationCharacter)
    {
        if (boundary.DeliveryKind == BoundaryDeliveryKind.UnicodeCharacter
            && boundary.Character is char unicodeCharacter
            && IsTargetPunctuation(unicodeCharacter))
        {
            punctuationCharacter = unicodeCharacter;
            return true;
        }

        punctuationCharacter = default;
        return false;
    }

    private static bool IsTargetPunctuation(char character)
        => character is ',' or '.' or '!' or '?' or ':' or ';';

    private void RecordSentenceContextToken(string token)
    {
        if (!IsProtectionEnabled())
        {
            _sentenceLanguageContext.Clear();
            return;
        }

        var policy = CachedPolicyState == AutomationPolicyState.Allowed
            ? CreateCachedAllowedPolicy()
            : _automationSafetyService.EvaluateCurrentContext();
        if (!IsPolicyAllowedForLiveCorrection(policy))
        {
            _sentenceLanguageContext.SetCollectionEnabled(false);
            return;
        }

        _sentenceLanguageContext.SetCollectionEnabled(true);
        _sentenceLanguageContext.RecordCompletedToken(token, policy);
    }

    private void RecordJointCorrectionSkipped(
        string token,
        JointCorrectionRecommendation recommendation,
        bool layoutEnabled,
        bool autocorrectEnabled)
    {
        if (!autocorrectEnabled && WouldAutocorrectIfEnabled(token))
        {
            IncrementAutocorrectBlocked();
            SetLastAction(LiveLayoutCorrectionAction.AutocorrectSkippedDisabled);
            return;
        }

        if (recommendation == JointCorrectionRecommendation.Wait)
        {
            if (autocorrectEnabled)
            {
                IncrementAutocorrectBlocked();
            }

            if (layoutEnabled)
            {
                IncrementCorrectionsBlocked();
            }

            SetLastAction(LiveLayoutCorrectionAction.AutocorrectSkippedDetection);
            return;
        }

        if (layoutEnabled)
        {
            IncrementCorrectionsBlocked();
        }

        if (autocorrectEnabled)
        {
            IncrementAutocorrectBlocked();
        }

        SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedDetection);
    }

    private bool WouldAutocorrectIfEnabled(string token)
    {
        var language = TokenScriptAnalyzer.Classify(token) switch
        {
            TokenScript.Latin => TypingLanguage.English,
            TokenScript.Cyrillic => TypingLanguage.Russian,
            _ => (TypingLanguage?)null,
        };

        if (language is null)
        {
            return false;
        }

        var result = _autocorrectionService.Evaluate(
            token,
            language.Value,
            _autocorrectDictionary,
            _autocorrectionOptions);

        return result.Recommendation == AutocorrectionRecommendation.Candidate;
    }

    private async Task ProcessEarlyLayoutCorrectionAsync(
        PreparedLayoutCorrection correction,
        CancellationToken cancellationToken)
    {
        if (_earlyLayoutCorrectionService is null
            || !IsProtectionEnabled()
            || !_settingsService.Current.AutomaticLayoutEnabled
            || _keyboardInputLanguageService is null
            || ProtectedTokenAnalyzer.IsProtected(correction.OriginalText)
            || !IsEarlyLayoutShapeValid(correction))
        {
            _earlyLayoutCorrectionService?.Complete(correction, false, false);
            IncrementCorrectionsBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedPolicy);
            return;
        }

        var bufferedToken = _tokenBuffer.Snapshot();
        var recentText = _recentTextContext.Snapshot();
        if (!string.Equals(bufferedToken, correction.OriginalText, StringComparison.Ordinal)
            || !recentText.EndsWith(correction.OriginalText, StringComparison.Ordinal))
        {
            _earlyLayoutCorrectionService.Complete(correction, false, false);
            IncrementCorrectionsBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedDetection);
            return;
        }

        var policy = await GetCurrentPolicyAsync(cancellationToken).ConfigureAwait(false);
        NotifyPolicyContextChanged(policy);
        if (!IsPolicyAllowedForLiveCorrection(policy))
        {
            _earlyLayoutCorrectionService.Complete(correction, false, false);
            IncrementCorrectionsBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedPolicy);
            return;
        }

        if (!_earlyLayoutCorrectionService.TryReserve(
                correction,
                _correctionApplicationContext.WindowHandle,
                inputBarrierOwned: true))
        {
            IncrementCorrectionsBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedDetection);
            return;
        }

        IncrementCandidatesDetected();
        IncrementCorrectionsAttempted();
        SetLastAction(LiveLayoutCorrectionAction.CorrectionAttempted);

        // The current character has already reached the target. The C# text
        // replacement session now holds later hook events while this batch is
        // sent, so no character can land between deletion and insertion.
        var replacement = await ExecuteReplacementAsync(
                correction.OriginalText,
                correction.ReplacementText,
                cancellationToken)
            .ConfigureAwait(false);

        if (replacement.Status != TextReplacementStatus.Success)
        {
            _earlyLayoutCorrectionService.Complete(correction, false, false);
            SetLastAction(LiveLayoutCorrectionAction.CorrectionFailed);
            return;
        }

        var layoutAcknowledged = await UpdateForegroundInputLanguageAsync(
                correction.TargetInputLanguage,
                cancellationToken)
            .ConfigureAwait(false);
        if (!layoutAcknowledged)
        {
            // A successful text rewrite without a confirmed layout switch is
            // unsafe: the next physical key would be decoded in the old
            // layout. The monitor's short early barrier is still active, so
            // roll the batch back before releasing deferred input.
            var rollback = await ExecuteReplacementAsync(
                    correction.ReplacementText,
                    correction.OriginalText,
                    cancellationToken)
                .ConfigureAwait(false);
            _earlyLayoutCorrectionService.Complete(correction, false, false);
            SetLastAction(LiveLayoutCorrectionAction.CorrectionFailed);
            if (rollback.Status != TextReplacementStatus.Success)
            {
                ResetBufferInternal(LiveLayoutCorrectionAction.CorrectionFailed);
            }

            return;
        }

        var tokenReconciled = _tokenBuffer.TryReplaceBuffer(
            correction.OriginalText,
            correction.ReplacementText);
        var contextReconciled = _recentTextContext.TryReplaceTrailing(
            correction.OriginalText,
            correction.ReplacementText);
        if (!tokenReconciled)
        {
            // This should only be reachable if an adapter violated the FIFO
            // contract. Avoid running the boundary detector on stale text.
            _earlyLayoutCorrectionService.Complete(correction, true, true);
            ResetBufferInternal(LiveLayoutCorrectionAction.CorrectionFailed);
            return;
        }

        _earlyLayoutCorrectionService.Complete(correction, true, true);
        _ = contextReconciled; // Best-effort rolling-context reconciliation.
        IncrementCorrectionsSucceeded();
        SetLastAction(LiveLayoutCorrectionAction.CorrectionSucceeded);
        _feedbackNotifier.NotifySuccessfulCorrection(CorrectionKind.Layout);
    }

    private static bool IsEarlyLayoutShapeValid(PreparedLayoutCorrection correction)
    {
        if (correction.OriginalText.Length < 4
            || correction.OriginalText.Length > EarlyLayoutModel.MaximumPrefixLength
            || string.IsNullOrEmpty(correction.ReplacementText)
            || correction.ReplacementText.Length != correction.OriginalText.Length)
        {
            return false;
        }

        Func<char, bool> expectedTargetScript = correction.TargetInputLanguage == KeyboardInputLanguage.Russian
            ? TokenScriptAnalyzer.IsCyrillicLetter
            : TokenScriptAnalyzer.IsLatinLetter;
        return correction.OriginalText.All(char.IsLetter)
            && correction.ReplacementText.All(expectedTargetScript);
    }

    private async Task ProcessPreparedLayoutCorrectionAsync(
        PreparedLayoutCorrection correction,
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        if (boundary is null
            || !IsProtectionEnabled()
            || !_settingsService.Current.AutomaticLayoutEnabled)
        {
            IncrementCorrectionsBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedDisabled);
            await DeliverDeferredBoundaryAsync(boundary, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            // The normal boundary path records the boundary before it starts
            // correction. Prepared layout corrections return through this
            // early branch, so preserve the same recent-text state for the
            // next word and for punctuation/Double Shift decisions.
            _recentTextContext.Apply(TokenInputEvent.Boundary(boundary));

            // A prepared correction is produced before the boundary reaches
            // the engine. If the buffer has since changed, applying the stale
            // pair would duplicate/corrupt text. Positive service anchors
            // retain the legacy direct-injection test path; normal live
            // corrections must match the current token exactly.
            var bufferedToken = _tokenBuffer.Snapshot();
            var hasPhysicalLayoutPunctuation =
                TokenScriptAnalyzer.HasEnglishLayoutPunctuation(correction.OriginalText);
            var bufferMatches = string.Equals(
                    bufferedToken,
                    correction.OriginalText,
                    StringComparison.OrdinalIgnoreCase)
                || (hasPhysicalLayoutPunctuation
                    && !string.IsNullOrEmpty(bufferedToken)
                    && correction.OriginalText.EndsWith(
                        bufferedToken,
                        StringComparison.OrdinalIgnoreCase));
            if ((!string.IsNullOrEmpty(bufferedToken) && !bufferMatches)
                || (string.IsNullOrEmpty(bufferedToken)
                    && !LayoutCorrectionAnchors.IsPositiveAnchor(correction.OriginalText)
                    && !hasPhysicalLayoutPunctuation))
            {
                IncrementCorrectionsBlocked();
                // The hook has already consumed the boundary and preflight
                // has already closed the word.  Keeping the stale Core token
                // here would append the next word to it and could cause a
                // second correction to delete the wrong number of characters.
                ResetBufferInternal(LiveLayoutCorrectionAction.CorrectionSkippedDetection);
                return;
            }

            var policy = await GetCurrentPolicyAsync(cancellationToken).ConfigureAwait(false);
            NotifyPolicyContextChanged(policy);

            if (!IsPolicyAllowedForLiveCorrection(policy))
            {
                IncrementCorrectionsBlocked();
                SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedPolicy);
                return;
            }

            if (_rejectionPolicy.IsAutomaticallySuppressed(
                    correction.OriginalText,
                    correction.ReplacementText,
                    CorrectionKind.Layout))
            {
                IncrementCorrectionsBlocked();
                SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedLearning);
                return;
            }

            if (!BoundedCandidateApplyGuard.AllowsPreparedLayout(
                    correction.OriginalText,
                    correction.ReplacementText,
                    _autocorrectDictionary,
                    new KeyboardLayoutConverter(),
                    _sentenceLanguageContext.GetHint()))
            {
                IncrementCorrectionsBlocked();
                SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedPolicy);
                return;
            }

            IncrementTokensCompleted();
            IncrementCandidatesDetected();
            IncrementCorrectionsAttempted();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionAttempted);

            var replacement = await ExecuteReplacementAsync(
                correction.OriginalText,
                correction.ReplacementText,
                cancellationToken).ConfigureAwait(false);

            if (replacement.Status == TextReplacementStatus.Success)
            {
                IncrementCorrectionsSucceeded();
                SetLastAction(LiveLayoutCorrectionAction.CorrectionSucceeded);
                RecordUndoTransaction(
                    correction.OriginalText,
                    correction.ReplacementText,
                    CorrectionKind.Layout,
                    boundary);
                _feedbackNotifier.NotifySuccessfulCorrection(CorrectionKind.Layout);
                await UpdateForegroundInputLanguageAsync(
                        correction.TargetInputLanguage,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                SetLastAction(LiveLayoutCorrectionAction.CorrectionFailed);
            }
        }
        finally
        {
            // Prepared layout candidates are produced by the hook-thread
            // preflight before Core receives the boundary.  Unlike the normal
            // boundary path, this branch returns early, so it must clear the
            // Core token/snippet buffers explicitly.  Otherwise the corrected
            // word remains in the internal buffer and the next word is
            // evaluated as a concatenated token.
            _tokenBuffer.ClearBuffer();
            _snippetTriggerBuffer.ClearBuffer();
            await DeliverDeferredBoundaryAsync(boundary, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<LiveCorrectionAttemptOutcome> TrySnippetExpansionAsync(
        string trigger,
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            return await TrySnippetExpansionCoreAsync(trigger, boundary, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _performanceMetrics.RecordDuration(
                PerformanceMetricKind.SnippetEvaluation,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private async Task<LiveCorrectionAttemptOutcome> TrySnippetExpansionCoreAsync(
        string trigger,
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        var language = TypingLanguageResolver.Resolve(trigger);
        var match = _snippetService.FindMatch(
            trigger,
            new SnippetMatchContext
            {
                Language = language,
                ApplicationProcessName = _correctionApplicationContext.ProcessName,
            });

        if (!match.IsMatch || match.Snippet is null || string.IsNullOrEmpty(match.Replacement))
        {
            IncrementSnippetBlocked();
            SetLastAction(LiveLayoutCorrectionAction.SnippetSkippedNoMatch);
            return LiveCorrectionAttemptOutcome.NotAttempted;
        }

        IncrementSnippetAttempted();
        SetLastAction(LiveLayoutCorrectionAction.SnippetAttempted);

        if (_rejectionPolicy.IsAutomaticallySuppressed(trigger, match.Replacement, CorrectionKind.Snippet))
        {
            IncrementSnippetBlocked();
            SetLastAction(LiveLayoutCorrectionAction.SnippetSkippedLearning);
            return LiveCorrectionAttemptOutcome.NotAttempted;
        }

        var replacement = await ExecuteReplacementAsync(
            trigger,
            match.Replacement,
            cancellationToken).ConfigureAwait(false);

        if (replacement.Status == TextReplacementStatus.Success)
        {
            IncrementSnippetSucceeded();
            SetLastAction(LiveLayoutCorrectionAction.SnippetSucceeded);
            RecordUndoTransaction(trigger, match.Replacement, CorrectionKind.Snippet, boundary);
            _feedbackNotifier.NotifySuccessfulCorrection(CorrectionKind.Snippet);
        }
        else
        {
            SetLastAction(LiveLayoutCorrectionAction.SnippetFailed);
        }

        return LiveCorrectionAttemptOutcome.Attempted;
    }

    private async Task<LiveCorrectionAttemptOutcome> TryLayoutCorrectionAsync(
        string token,
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            return await TryLayoutCorrectionCoreAsync(token, boundary, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _performanceMetrics.RecordDuration(
                PerformanceMetricKind.LayoutEvaluation,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private async Task<LiveCorrectionAttemptOutcome> TryLayoutCorrectionCoreAsync(
        string token,
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        var detection = _wrongLayoutDetectionService.Evaluate(
            token,
            ActiveLanguageSet.EnglishAndRussian);

        if (detection.Recommendation != LayoutDetectionRecommendation.Candidate)
        {
            IncrementCorrectionsBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedDetection);
            return LiveCorrectionAttemptOutcome.NotAttempted;
        }

        if (detection.ConfidenceScore < WrongLayoutDetectionOptions.DefaultCandidateThreshold)
        {
            IncrementCorrectionsBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedDetection);
            return LiveCorrectionAttemptOutcome.NotAttempted;
        }

        if (string.IsNullOrEmpty(detection.CandidateToken))
        {
            IncrementCorrectionsBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedDetection);
            return LiveCorrectionAttemptOutcome.NotAttempted;
        }

        IncrementCandidatesDetected();
        IncrementCorrectionsAttempted();
        SetLastAction(LiveLayoutCorrectionAction.CorrectionAttempted);

        if (_rejectionPolicy.IsAutomaticallySuppressed(token, detection.CandidateToken, CorrectionKind.Layout))
        {
            IncrementCorrectionsBlocked();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSkippedLearning);
            return LiveCorrectionAttemptOutcome.NotAttempted;
        }

        var replacement = await ExecuteReplacementAsync(
            token,
            detection.CandidateToken,
            cancellationToken).ConfigureAwait(false);

        if (replacement.Status == TextReplacementStatus.Success)
        {
            IncrementCorrectionsSucceeded();
            SetLastAction(LiveLayoutCorrectionAction.CorrectionSucceeded);
            RecordUndoTransaction(token, detection.CandidateToken, CorrectionKind.Layout, boundary);
            _feedbackNotifier.NotifySuccessfulCorrection(CorrectionKind.Layout);
            await UpdateForegroundInputLanguageAsync(
                    detection.ConversionDirection == LayoutConversionDirection.EnglishToRussian
                        ? KeyboardInputLanguage.Russian
                        : KeyboardInputLanguage.English,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            SetLastAction(LiveLayoutCorrectionAction.CorrectionFailed);
        }

        return LiveCorrectionAttemptOutcome.Attempted;
    }

    private async Task TryAutocorrectAsync(
        string token,
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await TryAutocorrectCoreAsync(token, boundary, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _performanceMetrics.RecordDuration(
                PerformanceMetricKind.AutocorrectEvaluation,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private async Task TryAutocorrectCoreAsync(
        string token,
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        var language = TypingLanguageResolver.Resolve(token);
        if (language is null)
        {
            IncrementAutocorrectBlocked();
            SetLastAction(LiveLayoutCorrectionAction.AutocorrectSkippedDetection);
            return;
        }

        var detection = _autocorrectionService.Evaluate(
            token,
            language.Value,
            _autocorrectDictionary,
            _autocorrectionOptions);

        if (detection.Recommendation != AutocorrectionRecommendation.Candidate
            || string.IsNullOrEmpty(detection.CandidateToken))
        {
            IncrementAutocorrectBlocked();
            SetLastAction(LiveLayoutCorrectionAction.AutocorrectSkippedDetection);
            return;
        }

        IncrementAutocorrectCandidatesDetected();
        IncrementAutocorrectAttempts();
        SetLastAction(LiveLayoutCorrectionAction.AutocorrectAttempted);

        if (_rejectionPolicy.IsAutomaticallySuppressed(token, detection.CandidateToken, CorrectionKind.Autocorrect))
        {
            IncrementAutocorrectBlocked();
            SetLastAction(LiveLayoutCorrectionAction.AutocorrectSkippedLearning);
            return;
        }

        var replacement = await ExecuteReplacementAsync(
            token,
            detection.CandidateToken,
            cancellationToken).ConfigureAwait(false);

        if (replacement.Status == TextReplacementStatus.Success)
        {
            IncrementAutocorrectSucceeded();
            SetLastAction(LiveLayoutCorrectionAction.AutocorrectSucceeded);
            RecordUndoTransaction(token, detection.CandidateToken, CorrectionKind.Autocorrect, boundary);
            _feedbackNotifier.NotifySuccessfulCorrection(CorrectionKind.Autocorrect);
            return;
        }

        SetLastAction(LiveLayoutCorrectionAction.AutocorrectFailed);
    }

    private void RecordUndoTransaction(
        string originalToken,
        string replacementToken,
        CorrectionKind kind,
        DeferredBoundaryKey? boundary)
    {
        var trailingText = GetUndoTrailingText(boundary);
        if (trailingText is null)
        {
            // Tab and Enter cannot be reproduced safely through Unicode text
            // replacement. Do not leave an older correction available to undo.
            _correctionUndoService.Invalidate(CorrectionUndoInvalidationReason.GenuineUserInput);
            return;
        }

        _correctionUndoService.RecordSuccessfulCorrection(new CorrectionTransaction
        {
            OriginalToken = originalToken,
            ReplacementToken = replacementToken,
            Kind = kind,
            TrailingText = trailingText,
            RecordedAt = DateTimeOffset.UtcNow,
            ApplicationProcessName = _correctionApplicationContext.ProcessName,
            ApplicationWindowHandle = _correctionApplicationContext.WindowHandle,
        });
    }

    private static string? GetUndoTrailingText(DeferredBoundaryKey? boundary)
    {
        if (boundary is null)
        {
            return string.Empty;
        }

        if (boundary.DeliveryKind == BoundaryDeliveryKind.UnicodeCharacter)
        {
            return boundary.Character?.ToString();
        }

        return boundary.VirtualKeyCode == VirtualKeys.Space ? " " : null;
    }

    private async Task<TextReplacementResult> ExecuteReplacementAsync(
        string originalToken,
        string replacementToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(_options.ReplacementTimeoutMilliseconds));

            return await _safeTextReplacementService
                .ReplaceRecentTextAsync(originalToken, replacementToken, timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TextReplacementResult.Failed("Replacement timed out.");
        }
    }

    private async Task<bool> UpdateForegroundInputLanguageAsync(
        KeyboardInputLanguage language,
        CancellationToken cancellationToken)
    {
        if (_keyboardInputLanguageService is null)
        {
            return false;
        }

        try
        {
            return await _keyboardInputLanguageService
                .SetForegroundInputLanguageAsync(language, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The text was already corrected. A missing target layout or an
            // app that ignores the request must not corrupt or undo that edit.
            return false;
        }
    }

    private async Task DeliverDeferredBoundaryAsync(
        DeferredBoundaryKey? boundary,
        CancellationToken cancellationToken)
    {
        if (boundary is null)
        {
            return;
        }

        await _boundaryKeyDeliveryService
            .DeliverAsync(boundary, cancellationToken)
            .ConfigureAwait(false);

        IncrementBoundariesDelivered();
    }

    private bool IsProtectionEnabled() => _settingsService.Current.IsEnabled;

    private async Task<AutomationPolicyResult> GetCurrentPolicyAsync(
        CancellationToken cancellationToken)
    {
        if (CachedPolicyState == AutomationPolicyState.Allowed)
        {
            return CreateCachedAllowedPolicy();
        }

        return await _automationSafetyService
            .EvaluateCurrentContextAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static AutomationPolicyResult CreateCachedAllowedPolicy()
    {
        return new AutomationPolicyResult
        {
            State = AutomationPolicyState.Allowed,
            AllowsAutomation = true,
            AllowsManualExternalTextOperations = true,
        };
    }

    private static bool IsPolicyAllowedForLiveCorrection(AutomationPolicyResult policy)
    {
        return policy.State == AutomationPolicyState.Allowed
            && policy.AllowsAutomation
            && !policy.IsEmergencyPaused;
    }

    private void ResetBufferInternal(
        LiveLayoutCorrectionAction action,
        bool preserveLastCompletedToken = false)
    {
        _earlyLayoutCorrectionService?.Reset();
        var hadContent = !_tokenBuffer.IsEmpty || !_snippetTriggerBuffer.IsEmpty;
        _tokenBuffer.ClearBuffer();
        _snippetTriggerBuffer.ClearBuffer();
        _recentTextContext.Clear(preserveLastCompletedToken);
        _sentenceLanguageContext.Clear();

        if (hadContent)
        {
            IncrementBufferResets();
        }

        SetLastAction(action);
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

        // Do not reduce a URL, path, email or dotted identifier to its final
        // alphabetic segment for a later explicit layout toggle.
        if (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            return string.Empty;
        }

        return start < end ? text[start..end] : string.Empty;
    }

    private LiveLayoutCorrectionStatus BuildStatus()
    {
        return new LiveLayoutCorrectionStatus
        {
            TokensCompleted = _tokensCompleted,
            CandidatesDetected = _candidatesDetected,
            CorrectionsAttempted = _correctionsAttempted,
            CorrectionsSucceeded = _correctionsSucceeded,
            CorrectionsBlocked = _correctionsBlocked,
            AutocorrectCandidatesDetected = _autocorrectCandidatesDetected,
            AutocorrectAttempts = _autocorrectAttempts,
            AutocorrectSucceeded = _autocorrectSucceeded,
            AutocorrectBlocked = _autocorrectBlocked,
            SnippetExpansionsAttempted = _snippetExpansionsAttempted,
            SnippetExpansionsSucceeded = _snippetExpansionsSucceeded,
            SnippetExpansionsBlocked = _snippetExpansionsBlocked,
            IsPunctuationEnabled = _settingsService.Current.PunctuationEnabled,
            PunctuationAttempts = _punctuationAttempts,
            PunctuationSucceeded = _punctuationSucceeded,
            PunctuationBlocked = _punctuationBlocked,
            BufferResets = _bufferResets,
            BoundariesSuppressed = _boundariesSuppressed,
            BoundariesDelivered = _boundariesDelivered,
            CurrentTokenLength = _tokenBuffer.Length,
            CurrentSnippetTriggerLength = _snippetTriggerBuffer.Length,
            LastPolicyState = _lastPolicyState,
            LastAction = _lastAction,
            IsAutomaticLayoutEnabled = _settingsService.Current.AutomaticLayoutEnabled,
            IsAutocorrectEnabled = _settingsService.Current.AutocorrectEnabled,
            IsSnippetsEnabled = _settingsService.Current.SnippetsEnabled,
            IsProtectionEnabled = _settingsService.Current.IsEnabled,
        };
    }

    private void IncrementTokensCompleted()
    {
        lock (_statusSync)
        {
            _tokensCompleted++;
        }
    }

    private void IncrementCandidatesDetected()
    {
        lock (_statusSync)
        {
            _candidatesDetected++;
        }
    }

    private void IncrementCorrectionsAttempted()
    {
        lock (_statusSync)
        {
            _correctionsAttempted++;
        }
    }

    private void IncrementCorrectionsSucceeded()
    {
        lock (_statusSync)
        {
            _correctionsSucceeded++;
        }
    }

    private void IncrementCorrectionsBlocked()
    {
        lock (_statusSync)
        {
            _correctionsBlocked++;
        }
    }

    private void IncrementAutocorrectCandidatesDetected()
    {
        lock (_statusSync)
        {
            _autocorrectCandidatesDetected++;
        }
    }

    private void IncrementAutocorrectAttempts()
    {
        lock (_statusSync)
        {
            _autocorrectAttempts++;
        }
    }

    private void IncrementAutocorrectSucceeded()
    {
        lock (_statusSync)
        {
            _autocorrectSucceeded++;
        }
    }

    private void IncrementAutocorrectBlocked()
    {
        lock (_statusSync)
        {
            _autocorrectBlocked++;
        }
    }

    private void IncrementSnippetAttempted()
    {
        lock (_statusSync)
        {
            _snippetExpansionsAttempted++;
        }
    }

    private void IncrementSnippetSucceeded()
    {
        lock (_statusSync)
        {
            _snippetExpansionsSucceeded++;
        }
    }

    private void IncrementSnippetBlocked()
    {
        lock (_statusSync)
        {
            _snippetExpansionsBlocked++;
        }
    }

    private void IncrementPunctuationAttempts()
    {
        lock (_statusSync)
        {
            _punctuationAttempts++;
        }
    }

    private void IncrementPunctuationSucceeded()
    {
        lock (_statusSync)
        {
            _punctuationSucceeded++;
        }
    }

    private void IncrementPunctuationBlocked()
    {
        lock (_statusSync)
        {
            _punctuationBlocked++;
        }
    }

    private void IncrementBufferResets()
    {
        lock (_statusSync)
        {
            _bufferResets++;
        }
    }

    private void IncrementBoundariesSuppressed()
    {
        lock (_statusSync)
        {
            _boundariesSuppressed++;
        }
    }

    private void IncrementBoundariesDelivered()
    {
        lock (_statusSync)
        {
            _boundariesDelivered++;
        }
    }

    private void SetLastAction(LiveLayoutCorrectionAction action)
    {
        lock (_statusSync)
        {
            _lastAction = action;
        }
    }

    private enum LiveCorrectionAttemptOutcome
    {
        NotAttempted,
        Attempted,
    }
}
