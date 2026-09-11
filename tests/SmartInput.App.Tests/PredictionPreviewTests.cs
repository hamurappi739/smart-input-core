using SmartInput.App.Services;
using SmartInput.App.ViewModels;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.App.Tests;

public class PredictionPreviewServiceTests
{
    [Fact]
    public void Evaluate_EligibleContext_ReturnsGhostSuffix()
    {
        var service = CreateService(EnabledSettings(), AllowedPolicy());

        var evaluation = service.Evaluate("how are ", 8, TypingLanguage.English, suggestionDismissed: false);

        Assert.Equal(PredictionPreviewDisplayState.Suggestion, evaluation.State);
        Assert.Equal("you", evaluation.GhostSuffix);
        Assert.True(evaluation.CanAccept);
        Assert.DoesNotContain("you", evaluation.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptSuggestion_InsertsGhostOnceAtCaret()
    {
        var service = CreateService(EnabledSettings(), AllowedPolicy());

        var accepted = service.AcceptSuggestion("how are ", 8, "you");

        Assert.Equal("how are you", accepted);
        Assert.Equal(11, service.GetCaretIndexAfterAccept(8, "you"));
    }

    [Fact]
    public void Evaluate_DismissedState_HidesGhostUntilRetyped()
    {
        var service = CreateService(EnabledSettings(), AllowedPolicy());

        var evaluation = service.Evaluate("how are ", 8, TypingLanguage.English, suggestionDismissed: true);

        Assert.False(evaluation.HasGhost);
        Assert.Equal(PredictionPreviewDisplayState.NoSuggestion, evaluation.State);
    }

    [Fact]
    public void Evaluate_PredictionDisabled_ReturnsDisabledState()
    {
        var service = CreateService(
            new AppSettings { IsEnabled = true, PredictionEnabled = false },
            AllowedPolicy());

        var evaluation = service.Evaluate("how are ", 8, TypingLanguage.English, suggestionDismissed: false);

        Assert.Equal(PredictionPreviewDisplayState.Disabled, evaluation.State);
        Assert.False(evaluation.HasGhost);
    }

    [Fact]
    public void Evaluate_SafeModePolicy_ReturnsPolicyBlockedState()
    {
        var service = CreateService(
            EnabledSettings(),
            new AutomationPolicyResult
            {
                State = AutomationPolicyState.SafeMode,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = true,
            });

        var evaluation = service.Evaluate("how are ", 8, TypingLanguage.English, suggestionDismissed: false);

        Assert.Equal(PredictionPreviewDisplayState.PolicyBlocked, evaluation.State);
        Assert.False(evaluation.HasGhost);
    }

    [Fact]
    public void Evaluate_ProtectedContext_ReturnsNoSuggestion()
    {
        var service = CreateService(EnabledSettings(), AllowedPolicy());

        var evaluation = service.Evaluate("GitHub ", 7, TypingLanguage.English, suggestionDismissed: false);

        Assert.Equal(PredictionPreviewDisplayState.NoSuggestion, evaluation.State);
        Assert.False(evaluation.HasGhost);
    }

    [Fact]
    public void Evaluate_MixedLanguageContext_ReturnsNoSuggestion()
    {
        var service = CreateService(EnabledSettings(), AllowedPolicy());

        var evaluation = service.Evaluate("hello привет ", 13, TypingLanguage.English, suggestionDismissed: false);

        Assert.Equal(PredictionPreviewDisplayState.NoSuggestion, evaluation.State);
        Assert.False(evaluation.HasGhost);
    }

    [Fact]
    public void Evaluate_LowConfidence_ReturnsLowConfidenceState()
    {
        var service = CreateService(
            EnabledSettings(),
            AllowedPolicy(),
            new FixedPredictionService(
            [
                new PredictionResult
                {
                    Context = "hello ",
                    ActiveLanguage = TypingLanguage.English,
                    Recommendation = PredictionRecommendation.LowConfidence,
                    Confidence = 0.40,
                    TokenCount = 1,
                    SuggestedContinuation = "maybe",
                },
            ]));

        var evaluation = service.Evaluate("hello ", 6, TypingLanguage.English, suggestionDismissed: false);

        Assert.Equal(PredictionPreviewDisplayState.LowConfidence, evaluation.State);
        Assert.Equal("maybe", evaluation.GhostSuffix);
        Assert.DoesNotContain("maybe", evaluation.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_MaximumSuggestionLength_ReturnsNoSuggestion()
    {
        var service = CreateService(
            EnabledSettings(),
            AllowedPolicy(),
            new FixedPredictionService(
            [
                new PredictionResult
                {
                    Context = "hello ",
                    ActiveLanguage = TypingLanguage.English,
                    Recommendation = PredictionRecommendation.NoSuggestion,
                    Confidence = 0.0,
                    TokenCount = 0,
                    SuggestedContinuation = null,
                },
            ]));

        var evaluation = service.Evaluate("hello ", 6, TypingLanguage.English, suggestionDismissed: false);

        Assert.Equal(PredictionPreviewDisplayState.NoSuggestion, evaluation.State);
        Assert.False(evaluation.HasGhost);
    }

    [Fact]
    public async Task Preview_DoesNotMutateLivePredictionEngine()
    {
        var liveEngine = new LivePredictionEngine(
            new PredictionService(new StarterLocalPredictionModel()),
            new FakeSettingsService(EnabledSettings()));
        liveEngine.NotifyPolicyContextChanged(AllowedPolicy());

        await liveEngine.ProcessInputAsync(TokenInputEvent.CharacterInput('h'));
        await liveEngine.ProcessInputAsync(TokenInputEvent.CharacterInput('i'));
        var before = liveEngine.Status;

        var preview = CreateService(EnabledSettings(), AllowedPolicy());
        preview.Evaluate("how are ", 8, TypingLanguage.English, suggestionDismissed: false);

        var after = liveEngine.Status;
        Assert.Equal(before.ContextCharacterCount, after.ContextCharacterCount);
        Assert.Equal(before.PredictionsEvaluated, after.PredictionsEvaluated);
        Assert.Equal(before.HasSuggestion, after.HasSuggestion);
    }

    private static PredictionPreviewService CreateService(
        AppSettings settings,
        AutomationPolicyResult policy,
        IPredictionService? predictionService = null)
    {
        return new PredictionPreviewService(
            predictionService ?? new PredictionService(new StarterLocalPredictionModel()),
            new FakeSettingsService(settings),
            new FakeAutomationSafetyService(policy));
    }

    private static AppSettings EnabledSettings()
    {
        return new AppSettings
        {
            IsEnabled = true,
            PredictionEnabled = true,
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

    private sealed class FixedPredictionService(IReadOnlyList<PredictionResult> results) : IPredictionService
    {
        public PredictionResult Predict(PredictionRequest request)
        {
            return results[0];
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

    private sealed class FakeAutomationSafetyService(AutomationPolicyResult policy) : IAutomationSafetyService
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
}

public class PredictionPreviewViewModelTests
{
    [Fact]
    public void AcceptSuggestion_UpdatesPreviewTextOnce()
    {
        var viewModel = CreateViewModel();

        viewModel.PreviewText = "how are ";
        viewModel.AcceptSuggestionCommand.Execute(null);

        Assert.Equal("how are you", viewModel.PreviewText);
        Assert.Equal(11, viewModel.CaretIndex);
        Assert.False(viewModel.HasGhostSuggestion);
    }

    [Fact]
    public void DismissSuggestion_ClearsGhostUntilTypingChanges()
    {
        var viewModel = CreateViewModel();

        viewModel.PreviewText = "how are ";
        Assert.True(viewModel.HasGhostSuggestion);

        viewModel.DismissSuggestionCommand.Execute(null);

        Assert.False(viewModel.HasGhostSuggestion);
        Assert.Contains("скрыта", viewModel.PreviewStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TypingChange_RefreshesSuggestionAfterDismiss()
    {
        var viewModel = CreateViewModel();

        viewModel.PreviewText = "how are ";
        viewModel.DismissSuggestionCommand.Execute(null);
        viewModel.PreviewText = "how are y";

        Assert.True(viewModel.HasGhostSuggestion);
        Assert.Equal("ou", viewModel.GhostSuffix);
    }

    [Fact]
    public void Backspace_RefreshesOrClearsSuggestion()
    {
        var viewModel = CreateViewModel();

        viewModel.PreviewText = "how are ";
        Assert.True(viewModel.HasGhostSuggestion);

        viewModel.PreviewText = string.Empty;

        Assert.False(viewModel.HasGhostSuggestion);
        Assert.Equal(PredictionPreviewDisplayState.Empty, viewModel.DisplayState);
    }

    [Fact]
    public void PreviewStatus_DoesNotContainGhostSuffixText()
    {
        var viewModel = CreateViewModel();

        viewModel.PreviewText = "how are ";
        viewModel.CaretIndex = 8;
        {
            Assert.DoesNotContain(viewModel.GhostSuffix, viewModel.PreviewStatus, StringComparison.Ordinal);
        }
    }

    private static PredictionPreviewViewModel CreateViewModel()
    {
        return new PredictionPreviewViewModel(new PredictionPreviewService(
            new PredictionService(new StarterLocalPredictionModel()),
            new FakeSettingsService(new AppSettings
            {
                IsEnabled = true,
                PredictionEnabled = true,
            }),
            new FakeAutomationSafetyService(new AutomationPolicyResult
            {
                State = AutomationPolicyState.Allowed,
                AllowsAutomation = true,
                AllowsManualExternalTextOperations = true,
            })));
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

    private sealed class FakeAutomationSafetyService(AutomationPolicyResult policy) : IAutomationSafetyService
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
}
