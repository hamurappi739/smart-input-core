using SmartInput.Core.Configuration;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

public interface ILivePredictionEngine
{
    LivePredictionStatus Status { get; }

    event Action? OverlayStateChanged;

    bool TryGetOverlaySnapshot(out LivePredictionOverlaySnapshot snapshot);

    bool TryBeginTabAcceptance(out PredictionTabAcceptanceAttempt attempt);

    void CompleteTabAcceptance(long version);

    void AbortTabAcceptance(long version);

    bool IsTabAcceptanceInProgress { get; }

    bool IsOverlayDismissedForCurrentContext { get; }

    bool TryDismissOverlaySuggestion();

    Task ProcessInputAsync(TokenInputEvent input, CancellationToken cancellationToken = default);

    void ResetBuffer(string reason);

    void NotifyPolicyContextChanged(AutomationPolicyResult policy);

    void NotifyApplicationContextChanged();
}

public sealed class LivePredictionEngine : ILivePredictionEngine
{
    private readonly IPredictionService _predictionService;
    private readonly ISettingsService _settingsService;
    private readonly PredictionContextBuffer _buffer = new();
    private readonly object _sync = new();

    private AutomationPolicyResult _lastPolicy = new()
    {
        State = AutomationPolicyState.UnknownContext,
        AllowsAutomation = false,
        AllowsManualExternalTextOperations = false,
    };

    private LivePredictionAction _lastAction = LivePredictionAction.None;
    private bool _hasSuggestion;
    private double _confidence;
    private PredictionRecommendation _recommendation = PredictionRecommendation.NoSuggestion;
    private int _suggestionTokenCount;
    private int _suggestionCharacterCount;
    private int _bufferResets;
    private int _predictionsEvaluated;
    private int _predictionsCleared;
    private string _overlaySuggestionText = string.Empty;
    private DateTimeOffset _overlayUpdatedAt;
    private long _overlayVersion;
    private bool _overlayNotifyPending;
    private bool _acceptanceInProgress;
    private long _acceptanceVersion;
    private bool _overlayDismissed;
    private int _dismissedContextCharacterCount;
    private int _dismissedContextWordCount;
    private bool _wasFeatureEnabled;

    public event Action? OverlayStateChanged;

    public LivePredictionEngine(
        IPredictionService predictionService,
        ISettingsService settingsService)
    {
        _predictionService = predictionService;
        _settingsService = settingsService;
        _wasFeatureEnabled = IsFeatureEnabled();
    }

    public bool IsTabAcceptanceInProgress
    {
        get
        {
            lock (_sync)
            {
                return _acceptanceInProgress;
            }
        }
    }

    public bool IsOverlayDismissedForCurrentContext
    {
        get
        {
            lock (_sync)
            {
                return IsDismissedForCurrentContextLocked();
            }
        }
    }

    public bool TryDismissOverlaySuggestion()
    {
        lock (_sync)
        {
            if (_acceptanceInProgress || !_hasSuggestion || string.IsNullOrEmpty(_overlaySuggestionText))
            {
                return false;
            }

            if (IsSuggestionExpiredLocked())
            {
                return false;
            }

            var snapshot = _buffer.CreateSnapshot();
            _overlayDismissed = true;
            _dismissedContextCharacterCount = snapshot.CharacterCount;
            _dismissedContextWordCount = snapshot.WordCount;
            ClearSuggestionState(LivePredictionAction.SuggestionDismissed);
            return true;
        }
    }

    public LivePredictionStatus Status
    {
        get
        {
            lock (_sync)
            {
                return BuildStatus();
            }
        }
    }

    public bool TryGetOverlaySnapshot(out LivePredictionOverlaySnapshot snapshot)
    {
        lock (_sync)
        {
            if (!_hasSuggestion || string.IsNullOrEmpty(_overlaySuggestionText))
            {
                snapshot = null!;
                return false;
            }

            snapshot = new LivePredictionOverlaySnapshot
            {
                SuggestionText = _overlaySuggestionText,
                UpdatedAt = _overlayUpdatedAt,
                Version = _overlayVersion,
            };

            return true;
        }
    }

    public bool TryBeginTabAcceptance(out PredictionTabAcceptanceAttempt attempt)
    {
        lock (_sync)
        {
            if (_acceptanceInProgress || !_hasSuggestion || string.IsNullOrEmpty(_overlaySuggestionText))
            {
                attempt = null!;
                return false;
            }

            if (IsSuggestionExpiredLocked())
            {
                attempt = null!;
                return false;
            }

            var snapshot = _buffer.CreateSnapshot();
            attempt = new PredictionTabAcceptanceAttempt
            {
                WordPrefix = snapshot.CurrentWordPrefix ?? string.Empty,
                SuggestionText = _overlaySuggestionText,
                Version = _overlayVersion,
                UpdatedAt = _overlayUpdatedAt,
            };

            _acceptanceInProgress = true;
            _acceptanceVersion = _overlayVersion;
            return true;
        }
    }

    public void CompleteTabAcceptance(long version)
    {
        lock (_sync)
        {
            if (!_acceptanceInProgress || version != _acceptanceVersion)
            {
                return;
            }

            var suggestionText = _overlaySuggestionText;
            AppendAcceptedSuggestionLocked(suggestionText);

            _acceptanceInProgress = false;
            _acceptanceVersion = 0;
            ClearSuggestionState(LivePredictionAction.SuggestionAccepted);
        }

        NotifyOverlayStateChangedIfPending();
    }

    public void AbortTabAcceptance(long version)
    {
        lock (_sync)
        {
            if (!_acceptanceInProgress)
            {
                return;
            }

            if (version != 0 && version != _acceptanceVersion)
            {
                return;
            }

            _acceptanceInProgress = false;
            _acceptanceVersion = 0;
            ClearSuggestionState(LivePredictionAction.PredictionCleared);
        }

        NotifyOverlayStateChangedIfPending();
    }

    public Task ProcessInputAsync(TokenInputEvent input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        lock (_sync)
        {
            var featureEnabled = IsFeatureEnabled();
            if (!featureEnabled && _wasFeatureEnabled)
            {
                ClearDismissedStateLocked();
            }
            else if (featureEnabled && !_wasFeatureEnabled)
            {
                ClearDismissedStateLocked();
            }

            _wasFeatureEnabled = featureEnabled;

            if (!featureEnabled)
            {
                _buffer.ClearBuffer(PredictionBufferResetReason.Disabled);
                ClearDismissedStateLocked();
                ClearSuggestionState(LivePredictionAction.SkippedDisabled);
            }
            else if (!IsPolicyAllowedForPrediction(_lastPolicy))
            {
                _buffer.ClearBuffer(PredictionBufferResetReason.PolicyBlocked);
                ClearDismissedStateLocked();
                ClearSuggestionState(LivePredictionAction.SkippedPolicy);
            }
            else
            {
                var applyResult = _buffer.Apply(input);
                if (applyResult.BufferReset)
                {
                    _bufferResets++;
                    ClearDismissedStateLocked();
                    ClearSuggestionState(LivePredictionAction.BufferReset);
                }
                else if (applyResult.BufferChanged)
                {
                    ClearDismissedStateLocked();
                    EvaluatePredictionLocked();
                }
            }
        }

        NotifyOverlayStateChangedIfPending();
        return Task.CompletedTask;
    }

    public void ResetBuffer(string reason)
    {
        lock (_sync)
        {
            _buffer.ClearBuffer(PredictionBufferResetReason.ManualReset);
            _bufferResets++;
            ClearDismissedStateLocked();
            ClearSuggestionState(LivePredictionAction.BufferReset);
        }

        NotifyOverlayStateChangedIfPending();
    }

    public void NotifyPolicyContextChanged(AutomationPolicyResult policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        lock (_sync)
        {
            var previousAllowed = IsPolicyAllowedForPrediction(_lastPolicy);
            _lastPolicy = policy;
            var currentAllowed = IsPolicyAllowedForPrediction(_lastPolicy);

            if (previousAllowed != currentAllowed || !currentAllowed)
            {
                _buffer.ClearBuffer(PredictionBufferResetReason.PolicyBlocked);
                _bufferResets++;
                ClearDismissedStateLocked();
                ClearSuggestionState(LivePredictionAction.SkippedPolicy);
            }
        }

        NotifyOverlayStateChangedIfPending();
    }

    public void NotifyApplicationContextChanged()
    {
        lock (_sync)
        {
            _buffer.ClearBuffer(PredictionBufferResetReason.FocusChange);
            _bufferResets++;
            ClearDismissedStateLocked();
            ClearSuggestionState(LivePredictionAction.BufferReset);
        }

        NotifyOverlayStateChangedIfPending();
    }

    private void EvaluatePredictionLocked()
    {
        if (IsDismissedForCurrentContextLocked())
        {
            if (_hasSuggestion)
            {
                ClearSuggestionState(LivePredictionAction.PredictionCleared);
            }

            return;
        }

        var snapshot = _buffer.CreateSnapshot();
        var activeLanguage = ResolveActiveLanguage(snapshot);
        if (activeLanguage is null)
        {
            ClearSuggestionState(LivePredictionAction.PredictionCleared);
            return;
        }

        var result = _predictionService.Predict(new PredictionRequest
        {
            Context = snapshot.Context,
            ActiveLanguage = activeLanguage.Value,
            CurrentWordPrefix = snapshot.CurrentWordPrefix,
        });

        _predictionsEvaluated++;

        if (result.Recommendation == PredictionRecommendation.NoSuggestion
            || string.IsNullOrEmpty(result.SuggestedContinuation))
        {
            ClearSuggestionState(LivePredictionAction.PredictionCleared);
            return;
        }

        _hasSuggestion = true;
        _confidence = result.Confidence;
        _recommendation = result.Recommendation;
        _suggestionTokenCount = result.TokenCount;
        _suggestionCharacterCount = result.SuggestedContinuation.Length;
        _overlaySuggestionText = BoundOverlaySuggestionText(result.SuggestedContinuation);
        _overlayUpdatedAt = DateTimeOffset.UtcNow;
        _overlayVersion++;
        _lastAction = LivePredictionAction.PredictionUpdated;
        MarkOverlayStateChanged();
    }

    private void ClearSuggestionState(LivePredictionAction action)
    {
        if (_hasSuggestion)
        {
            _predictionsCleared++;
        }

        _hasSuggestion = false;
        _confidence = 0.0;
        _recommendation = PredictionRecommendation.NoSuggestion;
        _suggestionTokenCount = 0;
        _suggestionCharacterCount = 0;
        _overlaySuggestionText = string.Empty;
        _overlayUpdatedAt = default;
        _overlayVersion++;
        _acceptanceInProgress = false;
        _acceptanceVersion = 0;
        _lastAction = action;
        MarkOverlayStateChanged();
    }

    private void MarkOverlayStateChanged()
    {
        _overlayNotifyPending = true;
    }

    private void NotifyOverlayStateChangedIfPending()
    {
        Action? handler;
        lock (_sync)
        {
            if (!_overlayNotifyPending)
            {
                return;
            }

            _overlayNotifyPending = false;
            handler = OverlayStateChanged;
        }

        handler?.Invoke();
    }

    private static string BoundOverlaySuggestionText(string suggestionText)
    {
        if (string.IsNullOrEmpty(suggestionText))
        {
            return string.Empty;
        }

        return suggestionText.Length <= PredictionOptions.DefaultMaxSuggestionCharacters
            ? suggestionText
            : suggestionText[..PredictionOptions.DefaultMaxSuggestionCharacters];
    }

    private void AppendAcceptedSuggestionLocked(string suggestionText)
    {
        foreach (var character in suggestionText)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            _buffer.Apply(TokenInputEvent.CharacterInput(character));
        }

        EvaluatePredictionLocked();
    }

    private void ClearDismissedStateLocked()
    {
        _overlayDismissed = false;
        _dismissedContextCharacterCount = 0;
        _dismissedContextWordCount = 0;
    }

    private bool IsDismissedForCurrentContextLocked()
    {
        if (!_overlayDismissed)
        {
            return false;
        }

        var snapshot = _buffer.CreateSnapshot();
        return snapshot.CharacterCount == _dismissedContextCharacterCount
            && snapshot.WordCount == _dismissedContextWordCount;
    }

    private bool IsSuggestionExpiredLocked()
    {
        if (_overlayUpdatedAt == default)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - _overlayUpdatedAt
            > TimeSpan.FromSeconds(PredictionOptions.DefaultSuggestionExpirySeconds);
    }

    private bool IsFeatureEnabled()
    {
        return _settingsService.Current.IsEnabled && _settingsService.Current.PredictionEnabled;
    }

    private static bool IsPolicyAllowedForPrediction(AutomationPolicyResult policy)
    {
        return policy.State == AutomationPolicyState.Allowed
            && policy.AllowsAutomation
            && !policy.IsEmergencyPaused;
    }

    private static TypingLanguage? ResolveActiveLanguage(PredictionContextSnapshot snapshot)
    {
        if (!string.IsNullOrEmpty(snapshot.CurrentWordPrefix))
        {
            return TypingLanguageResolver.Resolve(snapshot.CurrentWordPrefix);
        }

        for (var index = snapshot.Context.Length - 1; index >= 0; index--)
        {
            if (!char.IsLetter(snapshot.Context[index]))
            {
                continue;
            }

            var end = index;
            while (index >= 0 && char.IsLetter(snapshot.Context[index]))
            {
                index--;
            }

            var word = snapshot.Context[(index + 1)..(end + 1)];
            return TypingLanguageResolver.Resolve(word);
        }

        return null;
    }

    private LivePredictionStatus BuildStatus()
    {
        var snapshot = _buffer.CreateSnapshot();
        return new LivePredictionStatus
        {
            HasSuggestion = _hasSuggestion,
            Confidence = _confidence,
            Recommendation = _recommendation,
            SuggestionTokenCount = _suggestionTokenCount,
            SuggestionCharacterCount = _suggestionCharacterCount,
            ContextWordCount = snapshot.WordCount,
            ContextCharacterCount = snapshot.CharacterCount,
            LastPolicyState = _lastPolicy.State,
            LastAction = _lastAction,
            IsProtectionEnabled = _settingsService.Current.IsEnabled,
            IsPredictionEnabled = _settingsService.Current.PredictionEnabled,
            BufferResets = _bufferResets,
            PredictionsEvaluated = _predictionsEvaluated,
            PredictionsCleared = _predictionsCleared,
            IsOverlayDismissedForCurrentContext = IsDismissedForCurrentContextLocked(),
        };
    }
}
