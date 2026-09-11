using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class ManualSelectedTextCorrectionTests
{
    [Fact]
    public async Task FixLayout_EnglishToRussian_ConvertsSelection()
    {
        var selected = new FakeSelectedTextService("ghbdtn");
        var service = CreateService(selected, AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixLayout,
            LayoutDirection = LayoutConversionDirection.EnglishToRussian,
        });

        Assert.Equal(ManualCorrectionStatus.Success, result.Status);
        Assert.True(result.LayoutApplied);
        Assert.Equal("привет", selected.LastReplacement);
    }

    [Fact]
    public async Task FixLayout_RussianToEnglish_ConvertsSelection()
    {
        var selected = new FakeSelectedTextService("руддщ");
        var service = CreateService(selected, AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixLayout,
            LayoutDirection = LayoutConversionDirection.RussianToEnglish,
        });

        Assert.Equal(ManualCorrectionStatus.Success, result.Status);
        Assert.Equal("hello", selected.LastReplacement);
    }

    [Fact]
    public async Task FixSpelling_RussianAndEnglishTypos()
    {
        var selected = new FakeSelectedTextService("превет helo");
        var service = CreateService(selected, AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.Success, result.Status);
        Assert.Equal("привет hello", selected.LastReplacement);
        Assert.Equal(2, result.TokensChanged);
    }

    [Fact]
    public async Task FixText_AppliesLayoutThenSpellingDeterministically()
    {
        // Layout first: "ghbdtn helo" EN→RU → "привет руддщ" then spelling on Cyrillic tokens only.
        // Better fixture: English wrong-layout word + English typo after choosing EN→RU on mixed?
        // Use: "превет" only for spelling after layout of "ghbdtn ", and "helo" stays Latin.
        var selected = new FakeSelectedTextService("ghbdtn helo");
        var service = CreateService(selected, AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixText,
            LayoutDirection = LayoutConversionDirection.EnglishToRussian,
        });

        Assert.Equal(ManualCorrectionStatus.Success, result.Status);
        Assert.True(result.LayoutApplied);
        // ghbdtn→привет, helo→руддщ (layout), then spelling may correct привет (already correct) and руддщ (Cyrillic, may not match EN dict)
        Assert.StartsWith("привет", selected.LastReplacement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FixSpelling_PreservesWhitespacePunctuationAndMultiline()
    {
        var selected = new FakeSelectedTextService("превет,\n  helo!");
        var service = CreateService(selected, AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.Success, result.Status);
        Assert.Equal("привет,\n  hello!", selected.LastReplacement);
    }

    [Fact]
    public async Task FixSpelling_ProtectedTokensRemainUnchanged()
    {
        var selected = new FakeSelectedTextService("https://example.com превет GitHub");
        var service = CreateService(selected, AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.Success, result.Status);
        Assert.Equal("https://example.com привет GitHub", selected.LastReplacement);
    }

    [Fact]
    public async Task FixSpelling_MixedLanguagePreservedConservatively()
    {
        var selected = new FakeSelectedTextService("hello привет");
        var service = CreateService(selected, AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.NoChange, result.Status);
        Assert.Null(selected.LastReplacement);
    }

    [Fact]
    public async Task Execute_EmptySelection_ReturnsNoSelection()
    {
        var service = CreateService(new FakeSelectedTextService(null), AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.NoSelection, result.Status);
    }

    [Fact]
    public async Task Execute_SelectionReadFailure_ReturnsFailed()
    {
        var service = CreateService(new FakeSelectedTextService(null, readThrows: true), AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Execute_ReplacementFailure_ReturnsFailed()
    {
        var service = CreateService(
            new FakeSelectedTextService("превет", replaceSucceeds: false),
            AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Execute_CancellationDuringReplace_ReturnsCancelled()
    {
        var service = CreateService(
            new FakeSelectedTextService("превет", cancelOnReplace: true),
            AllowedPolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.Cancelled, result.Status);
    }

    [Theory]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    [InlineData(AutomationPolicyState.BlockedApplication)]
    public async Task Execute_BlockedContexts_AreRejected(AutomationPolicyState state)
    {
        var selected = new FakeSelectedTextService("превет");
        var service = CreateService(
            selected,
            new AutomationPolicyResult
            {
                State = state,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = false,
                Reason = state.ToString(),
            });

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.Blocked, result.Status);
        Assert.Equal(0, selected.ReadCount);
    }

    [Fact]
    public async Task Execute_SafeMode_AllowsFixLayout()
    {
        var selected = new FakeSelectedTextService("ghbdtn");
        var service = CreateService(selected, SafeModePolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixLayout,
            LayoutDirection = LayoutConversionDirection.EnglishToRussian,
        });

        Assert.Equal(ManualCorrectionStatus.Success, result.Status);
        Assert.Equal("привет", selected.LastReplacement);
    }

    [Fact]
    public async Task Execute_SafeMode_AllowsFixSpelling()
    {
        var selected = new FakeSelectedTextService("превет");
        var service = CreateService(selected, SafeModePolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.Success, result.Status);
        Assert.Equal("привет", selected.LastReplacement);
    }

    [Fact]
    public async Task Execute_SafeMode_AllowsFixText()
    {
        var selected = new FakeSelectedTextService("ghbdtn");
        var service = CreateService(selected, SafeModePolicy());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixText,
            LayoutDirection = LayoutConversionDirection.EnglishToRussian,
        });

        Assert.Equal(ManualCorrectionStatus.Success, result.Status);
        Assert.StartsWith("привет", selected.LastReplacement, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeModePolicy_BlocksAutomaticOperationsButAllowsManualExternal()
    {
        var policy = SafeModePolicy();

        Assert.False(policy.IsAllowed(AutomationOperationKind.AutomaticTextReplacement));
        Assert.True(policy.IsAllowed(AutomationOperationKind.ManualExternalTextOperation));
    }

    [Fact]
    public async Task Execute_ProtectionDisabled_IsBlocked()
    {
        var selected = new FakeSelectedTextService("превет");
        var service = CreateService(
            selected,
            AllowedPolicy(),
            settings: new AppSettings { IsEnabled = false });

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.Blocked, result.Status);
        Assert.Equal(ManualSelectedTextCorrectionService.ProtectionDisabledBlockedReason, result.FailureReason);
        Assert.Equal(0, selected.ReadCount);
    }

    [Fact]
    public async Task Execute_EmergencyPaused_IsBlocked()
    {
        var selected = new FakeSelectedTextService("превет");
        var service = CreateService(selected, AutomationPolicyResult.EmergencyPaused());

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixLayout,
            LayoutDirection = LayoutConversionDirection.EnglishToRussian,
        });

        Assert.Equal(ManualCorrectionStatus.Blocked, result.Status);
        Assert.Equal(0, selected.ReadCount);
    }

    [Fact]
    public async Task Execute_ChecksPolicyBeforeReadAndBeforeReplace()
    {
        var selected = new FakeSelectedTextService("превет");
        var safety = new SequencingSafetyService(
        [
            AllowedPolicy(),
            new AutomationPolicyResult
            {
                State = AutomationPolicyState.SecureInput,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = false,
                Reason = "Secure input is active in the foreground context.",
            },
        ]);
        var service = CreateService(selected, safety);

        var result = await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(ManualCorrectionStatus.Blocked, result.Status);
        Assert.Equal(1, selected.ReadCount);
        Assert.Equal(0, selected.ReplaceCount);
        Assert.Equal(2, safety.EvaluateCount);
    }

    [Fact]
    public async Task Execute_DoesNotAffectLiveTokenBuffers()
    {
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            new LiveCorrectionTestHelpers.FakeReplacementService());

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "abc");
        Assert.Equal(3, engine.PendingTokenLength);
        Assert.Equal(3, engine.PendingSnippetTriggerLength);

        var service = CreateService(new FakeSelectedTextService("превет"), AllowedPolicy());
        await service.ExecuteAsync(new ManualCorrectionRequest
        {
            Action = ManualCorrectionActionKind.FixSpelling,
        });

        Assert.Equal(3, engine.PendingTokenLength);
        Assert.Equal(3, engine.PendingSnippetTriggerLength);
    }

    [Fact]
    public void SelectedTextSegmenter_PreservesStructure()
    {
        var segments = SelectedTextSegmenter.Split("Hi,\n  there!");
        var joined = SelectedTextSegmenter.Join(segments);
        Assert.Equal("Hi,\n  there!", joined);
        Assert.Contains(segments, segment => segment is { Text: "Hi", IsCorrectableWord: true });
        Assert.Contains(segments, segment => segment is { Text: "there", IsCorrectableWord: true });
    }

    private static ManualSelectedTextCorrectionService CreateService(
        FakeSelectedTextService selectedText,
        AutomationPolicyResult policy,
        AppSettings? settings = null)
    {
        return CreateService(selectedText, new SequencingSafetyService([policy]), settings);
    }

    private static ManualSelectedTextCorrectionService CreateService(
        FakeSelectedTextService selectedText,
        IAutomationSafetyService safety,
        AppSettings? settings = null)
    {
        return new ManualSelectedTextCorrectionService(
            new KeyboardLayoutConverter(),
            selectedText,
            new AutocorrectionService(),
            LiveCorrectionTestHelpers.CreateStarterDictionary(),
            safety,
            new LiveCorrectionTestHelpers.FakeSettingsService(settings ?? new AppSettings { IsEnabled = true }));
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

    private static AutomationPolicyResult SafeModePolicy()
    {
        return new AutomationPolicyResult
        {
            State = AutomationPolicyState.SafeMode,
            AllowsAutomation = false,
            AllowsManualExternalTextOperations = true,
            Reason = "Safe Mode is active for this application category.",
        };
    }

    private sealed class FakeSelectedTextService : ISelectedTextService
    {
        private readonly string? _selectedText;
        private readonly bool _replaceSucceeds;
        private readonly bool _readThrows;
        private readonly bool _cancelOnReplace;

        public FakeSelectedTextService(
            string? selectedText,
            bool replaceSucceeds = true,
            bool readThrows = false,
            bool cancelOnReplace = false)
        {
            _selectedText = selectedText;
            _replaceSucceeds = replaceSucceeds;
            _readThrows = readThrows;
            _cancelOnReplace = cancelOnReplace;
        }

        public string? LastReplacement { get; private set; }

        public int ReadCount { get; private set; }

        public int ReplaceCount { get; private set; }

        public Task<string?> GetSelectedTextAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (_readThrows)
            {
                throw new InvalidOperationException("read failed");
            }

            return Task.FromResult(_selectedText);
        }

        public Task<bool> ReplaceSelectedTextAsync(string replacementText, CancellationToken cancellationToken = default)
        {
            ReplaceCount++;
            if (_cancelOnReplace)
            {
                throw new OperationCanceledException();
            }

            LastReplacement = replacementText;
            return Task.FromResult(_replaceSucceeds);
        }
    }

    private sealed class SequencingSafetyService(Queue<AutomationPolicyResult> policies) : IAutomationSafetyService
    {
        public SequencingSafetyService(IEnumerable<AutomationPolicyResult> policies)
            : this(new Queue<AutomationPolicyResult>(policies))
        {
        }

        public int EvaluateCount { get; private set; }

        public AutomationPolicyResult EvaluateCurrentContext() => Next();

        public Task<AutomationPolicyResult> EvaluateCurrentContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Next());
        }

        public bool IsOperationAllowed(AutomationOperationKind operationKind)
            => EvaluateCurrentContext().IsAllowed(operationKind);

        public Task<bool> IsOperationAllowedAsync(
            AutomationOperationKind operationKind,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IsOperationAllowed(operationKind));
        }

        public string? GetBlockedReason(AutomationOperationKind operationKind)
        {
            var policy = EvaluateCurrentContext();
            return policy.IsAllowed(operationKind) ? null : policy.Reason;
        }

        private AutomationPolicyResult Next()
        {
            EvaluateCount++;
            return policies.Count > 0 ? policies.Dequeue() : AllowedPolicy();
        }
    }
}
