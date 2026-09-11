using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Core.Configuration;

namespace SmartInput.Core.Tests;

public class LivePredictionEngineTests
{
    [Fact]
    public async Task ProcessInput_AllowedContext_UpdatesSuggestionMetadata()
    {
        var engine = CreateEngine(EnabledSettings(), AllowedPolicy());

        await TypePhraseAsync(engine, "how are ");

        var status = engine.Status;
        Assert.True(status.HasSuggestion);
        Assert.Equal(PredictionRecommendation.Suggestion, status.Recommendation);
        Assert.True(status.Confidence > 0);
        Assert.Equal(1, status.SuggestionTokenCount);
        Assert.True(status.SuggestionCharacterCount > 0);
        Assert.Equal(LivePredictionAction.PredictionUpdated, status.LastAction);
    }

    [Fact]
    public async Task ProcessInput_DeterministicUpdates_ProduceSameMetadata()
    {
        var engineA = CreateEngine(EnabledSettings(), AllowedPolicy());
        var engineB = CreateEngine(EnabledSettings(), AllowedPolicy());

        await TypePhraseAsync(engineA, "how are ");
        await TypePhraseAsync(engineB, "how are ");

        var statusA = engineA.Status;
        var statusB = engineB.Status;

        Assert.Equal(statusA.HasSuggestion, statusB.HasSuggestion);
        Assert.Equal(statusA.Recommendation, statusB.Recommendation);
        Assert.Equal(statusA.Confidence, statusB.Confidence);
        Assert.Equal(statusA.SuggestionTokenCount, statusB.SuggestionTokenCount);
        Assert.Equal(statusA.SuggestionCharacterCount, statusB.SuggestionCharacterCount);
    }

    [Fact]
    public async Task ProcessInput_PredictionDisabled_SkipsPredictionAndClearsBuffer()
    {
        var engine = CreateEngine(
            new AppSettings { IsEnabled = true, PredictionEnabled = false },
            AllowedPolicy());

        await TypePhraseAsync(engine, "how are ");

        var status = engine.Status;
        Assert.False(status.HasSuggestion);
        Assert.Equal(LivePredictionAction.SkippedDisabled, status.LastAction);
        Assert.Equal(0, status.ContextCharacterCount);
    }

    [Fact]
    public async Task ProcessInput_ProtectionDisabled_SkipsPredictionAndClearsBuffer()
    {
        var engine = CreateEngine(
            new AppSettings { IsEnabled = false, PredictionEnabled = true },
            AllowedPolicy());

        await TypePhraseAsync(engine, "how are ");

        var status = engine.Status;
        Assert.False(status.HasSuggestion);
        Assert.Equal(LivePredictionAction.SkippedDisabled, status.LastAction);
        Assert.Equal(0, status.ContextCharacterCount);
    }

    [Theory]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    [InlineData(AutomationPolicyState.SafeMode)]
    [InlineData(AutomationPolicyState.BlockedApplication)]
    public async Task ProcessInput_BlockedPolicy_DoesNotPredict(AutomationPolicyState state)
    {
        var engine = CreateEngine(
            EnabledSettings(),
            new AutomationPolicyResult
            {
                State = state,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = state == AutomationPolicyState.SafeMode,
            });

        await TypePhraseAsync(engine, "how are ");

        var status = engine.Status;
        Assert.False(status.HasSuggestion);
        Assert.Equal(LivePredictionAction.SkippedPolicy, status.LastAction);
        Assert.Equal(0, status.ContextCharacterCount);
    }

    [Fact]
    public async Task ProcessInput_EmergencyPause_DoesNotPredict()
    {
        var engine = CreateEngine(EnabledSettings(), AutomationPolicyResult.EmergencyPaused());

        await TypePhraseAsync(engine, "how are ");

        var status = engine.Status;
        Assert.False(status.HasSuggestion);
        Assert.Equal(LivePredictionAction.SkippedPolicy, status.LastAction);
    }

    [Fact]
    public async Task NotifyApplicationContextChanged_ClearsBufferAndSuggestion()
    {
        var engine = CreateEngine(EnabledSettings(), AllowedPolicy());

        await TypePhraseAsync(engine, "how are ");
        engine.NotifyApplicationContextChanged();

        var status = engine.Status;
        Assert.False(status.HasSuggestion);
        Assert.Equal(0, status.ContextCharacterCount);
        Assert.Equal(LivePredictionAction.BufferReset, status.LastAction);
    }

    [Fact]
    public async Task NotifyPolicyContextChanged_ToBlocked_ClearsBufferAndSuggestion()
    {
        var engine = CreateEngine(EnabledSettings(), AllowedPolicy());

        await TypePhraseAsync(engine, "how are ");
        engine.NotifyPolicyContextChanged(new AutomationPolicyResult
        {
            State = AutomationPolicyState.SecureInput,
            AllowsAutomation = false,
            AllowsManualExternalTextOperations = false,
        });

        var status = engine.Status;
        Assert.False(status.HasSuggestion);
        Assert.Equal(0, status.ContextCharacterCount);
        Assert.Equal(LivePredictionAction.SkippedPolicy, status.LastAction);
    }

    [Fact]
    public void Status_DoesNotExposeContextOrSuggestionText()
    {
        var properties = typeof(LivePredictionStatus)
            .GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.DoesNotContain(typeof(string), properties);
    }

    [Fact]
    public async Task TryGetOverlaySnapshot_WithSuggestion_ReturnsBoundedText()
    {
        var engine = CreateEngine(EnabledSettings(), AllowedPolicy());

        await TypePhraseAsync(engine, "how are ");

        Assert.True(engine.TryGetOverlaySnapshot(out var snapshot));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.SuggestionText));
        Assert.True(snapshot.SuggestionText.Length <= PredictionOptions.DefaultMaxSuggestionCharacters);
        Assert.True(snapshot.Version > 0);
        Assert.NotEqual(default, snapshot.UpdatedAt);
    }

    [Fact]
    public async Task TryGetOverlaySnapshot_AfterClear_ReturnsFalse()
    {
        var engine = CreateEngine(EnabledSettings(), AllowedPolicy());

        await TypePhraseAsync(engine, "how are ");
        engine.NotifyApplicationContextChanged();

        Assert.False(engine.TryGetOverlaySnapshot(out _));
    }

    [Fact]
    public async Task OverlayStateChanged_FiresWhenSuggestionUpdates()
    {
        var engine = CreateEngine(EnabledSettings(), AllowedPolicy());
        var notifications = 0;
        engine.OverlayStateChanged += () => notifications++;

        await TypePhraseAsync(engine, "how are ");

        Assert.True(notifications >= 1);
    }

    [Fact]
    public async Task ProcessInput_UncertainInput_ClearsSuggestion()
    {
        var engine = CreateEngine(EnabledSettings(), AllowedPolicy());

        await TypePhraseAsync(engine, "how are ");
        Assert.True(engine.Status.HasSuggestion);

        await engine.ProcessInputAsync(TokenInputEvent.Uncertain);

        var status = engine.Status;
        Assert.False(status.HasSuggestion);
        Assert.Equal(LivePredictionAction.BufferReset, status.LastAction);
    }

    private static LivePredictionEngine CreateEngine(AppSettings settings, AutomationPolicyResult policy)
    {
        var engine = new LivePredictionEngine(
            new PredictionService(new StarterLocalPredictionModel()),
            new FakeSettingsService(settings));

        engine.NotifyPolicyContextChanged(policy);
        return engine;
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

    private static async Task TypePhraseAsync(LivePredictionEngine engine, string phrase)
    {
        foreach (var character in phrase)
        {
            if (char.IsLetter(character))
            {
                await engine.ProcessInputAsync(TokenInputEvent.CharacterInput(character));
            }
            else if (char.IsWhiteSpace(character))
            {
                await engine.ProcessInputAsync(TokenInputEvent.Boundary(
                    DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));
            }
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
}
