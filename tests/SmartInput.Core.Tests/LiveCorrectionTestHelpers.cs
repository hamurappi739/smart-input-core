using SmartInput.Core.Configuration;
using SmartInput.Core.Diagnostics;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Integration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

internal static class LiveCorrectionTestHelpers
{
    internal static AutomaticLayoutCorrectionEngine CreateEngine(
        AutomationPolicyResult policy,
        AppSettings settings,
        ISafeTextReplacementService replacement,
        IBoundaryKeyDeliveryService? delivery = null,
        IAutocorrectDictionary? dictionary = null,
        LayoutCorrectionOptions? layoutOptions = null,
        ICorrectionUndoService? undoService = null,
        ICorrectionApplicationContext? applicationContext = null,
        ISnippetService? snippetService = null,
        ICorrectionRejectionPolicy? rejectionPolicy = null,
        ICorrectionFeedbackNotifier? feedbackNotifier = null,
        IKeyboardInputLanguageService? keyboardInputLanguageService = null,
        IPunctuationCorrectionService? punctuationCorrectionService = null,
        RecentTextContextBuffer? recentTextContext = null,
        IKbmAllowListCorrectionService? kbmAllowList = null,
        IRustHybridCorrectionService? rustHybrid = null,
        IEarlyLayoutCorrectionService? earlyLayoutCorrectionService = null)
    {
        return new AutomaticLayoutCorrectionEngine(
            new WrongLayoutDetectionService(
                new KeyboardLayoutConverter(),
                dictionary ?? CreateStarterDictionary()),
            new AutocorrectionService(),
            dictionary ?? CreateStarterDictionary(),
            snippetService ?? CreateEmptySnippetService(),
            replacement,
            new FakeAutomationSafetyService(policy),
            new FakeSettingsService(settings),
            delivery ?? new FakeBoundaryDeliveryService(),
            undoService ?? new FakeCorrectionUndoService(),
            applicationContext ?? new CorrectionApplicationContext(),
            NullPerformanceMetricsRecorder.Instance,
            layoutOptions,
            keyboardInputLanguageService: keyboardInputLanguageService,
            rejectionPolicy: rejectionPolicy,
            feedbackNotifier: feedbackNotifier,
            punctuationCorrectionService: punctuationCorrectionService,
            recentTextContext: recentTextContext,
            kbmAllowList: kbmAllowList,
            rustHybrid: rustHybrid,
            earlyLayoutCorrectionService: earlyLayoutCorrectionService);
    }

    internal static ISnippetService CreateEmptySnippetService()
    {
        return new SnippetService(new EmptySnippetPersistence());
    }

    internal sealed class EmptySnippetPersistence : Persistence.ISnippetPersistence
    {
        public Task<IReadOnlyList<SnippetDefinition>> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SnippetDefinition>>([]);

        public Task SaveAsync(IReadOnlyList<SnippetDefinition> snippets, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    internal static CompositeAutocorrectDictionary CreateStarterDictionary()
    {
        return new CompositeAutocorrectDictionary(
            new FakeUserAutocorrectDictionaryStore([]));
    }

    internal static CompositeAutocorrectDictionary CreateProductionLikeDictionary()
    {
        return new CompositeAutocorrectDictionary(
            new FakeUserAutocorrectDictionaryStore([]),
            new HunspellWordFormProvider());
    }

    internal static AppSettings EnabledLayoutSettings()
    {
        return new AppSettings
        {
            IsEnabled = true,
            AutomaticLayoutEnabled = true,
            AutocorrectEnabled = false,
        };
    }

    internal static AppSettings EnabledAutocorrectSettings(bool layoutEnabled = false)
    {
        return new AppSettings
        {
            IsEnabled = true,
            AutomaticLayoutEnabled = layoutEnabled,
            AutocorrectEnabled = true,
        };
    }

    internal static AppSettings EnabledBothCorrectionSettings()
    {
        return new AppSettings
        {
            IsEnabled = true,
            AutomaticLayoutEnabled = true,
            AutocorrectEnabled = true,
        };
    }

    internal static AppSettings EnabledPunctuationSettings(bool layoutEnabled = false, bool autocorrectEnabled = false)
    {
        return new AppSettings
        {
            IsEnabled = true,
            AutomaticLayoutEnabled = layoutEnabled,
            AutocorrectEnabled = autocorrectEnabled,
            PunctuationEnabled = true,
        };
    }

    internal static DeferredBoundaryKey SpaceBoundaryKey()
        => DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57);

    internal static DeferredBoundaryKey PunctuationBoundaryKey(char punctuation)
        => DeferredBoundaryKey.FromUnicodeCharacter(VirtualKeys.OemComma, 0, punctuation);

    internal static async Task TypeTextSpaceThenPunctuationAsync(
        IAutomaticLayoutCorrectionEngine engine,
        string textBeforeSpace,
        char punctuation)
    {
        await TypeTokenAsync(engine, textBeforeSpace);
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(SpaceBoundaryKey()));
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(PunctuationBoundaryKey(punctuation)));
    }

    internal static AutomationPolicyResult AllowedPolicy()
    {
        return Policy(AutomationPolicyState.Allowed, allowsAutomation: true);
    }

    internal static AutomationPolicyResult Policy(AutomationPolicyState state, bool allowsAutomation)
    {
        return new AutomationPolicyResult
        {
            State = state,
            AllowsAutomation = allowsAutomation,
            AllowsManualExternalTextOperations = state != AutomationPolicyState.SecureInput,
        };
    }

    internal static async Task TypeTokenAsync(IAutomaticLayoutCorrectionEngine engine, string token)
    {
        foreach (var character in token)
        {
            await engine.ProcessInputAsync(TokenInputEvent.CharacterInput(character));
        }
    }

    internal sealed class FakeSettingsService(AppSettings settings) : ISettingsService
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

    internal sealed class FakeAutomationSafetyService(AutomationPolicyResult policy) : IAutomationSafetyService
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

    internal sealed class FakeReplacementService : ISafeTextReplacementService
    {
        private readonly Func<string, string, TextReplacementResult> _handler;

        public FakeReplacementService(bool success = true)
            : this((original, replacement) => success
                ? TextReplacementResult.Success(original.Length, replacement.Length)
                : TextReplacementResult.Blocked("blocked"))
        {
        }

        public FakeReplacementService(Func<string, string, TextReplacementResult> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public string? LastOriginal { get; private set; }

        public string? LastReplacement { get; private set; }

        public Task<TextReplacementResult> ReplaceRecentTextAsync(
            string originalText,
            string replacementText,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOriginal = originalText;
            LastReplacement = replacementText;
            return Task.FromResult(_handler(originalText, replacementText));
        }

        public Task<TextReplacementResult> RunAbcToXyzDemoAsync(CancellationToken cancellationToken = default)
        {
            return ReplaceRecentTextAsync("abc", "xyz", cancellationToken);
        }

        public Task<TextReplacementResult> InsertTextAsync(string text, CancellationToken cancellationToken = default)
        {
            return ReplaceRecentTextAsync(string.Empty, text, cancellationToken);
        }
    }

    internal sealed class FakeBoundaryDeliveryService : IBoundaryKeyDeliveryService
    {
        public int DeliverCallCount { get; private set; }

        public Task DeliverAsync(DeferredBoundaryKey boundary, CancellationToken cancellationToken = default)
        {
            DeliverCallCount++;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeUserAutocorrectDictionaryStore(IReadOnlyList<UserAutocorrectDictionaryEntry> entries)
        : IUserAutocorrectDictionaryStore
    {
        public IReadOnlyList<UserAutocorrectDictionaryEntry> Entries => entries;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    internal sealed class FakeCorrectionUndoService : ICorrectionUndoService
    {
        public CorrectionUndoStatus Status { get; private set; } = new();

        public bool IsUndoAvailable => Status.IsAvailable;

        public CorrectionKind? PendingCorrectionKind => Status.PendingKind;

        public CorrectionTransaction? LastRecordedTransaction { get; private set; }

        public int RecordCount { get; private set; }

        public void RecordSuccessfulCorrection(CorrectionTransaction transaction)
        {
            LastRecordedTransaction = transaction;
            RecordCount++;
            Status = new CorrectionUndoStatus
            {
                IsAvailable = true,
                PendingKind = transaction.Kind,
            };
        }

        public void Invalidate(CorrectionUndoInvalidationReason reason)
        {
            _ = reason;
            Status = new CorrectionUndoStatus
            {
                IsAvailable = false,
                PendingKind = null,
            };
        }

        public void NotifyGenuineUserInput() => Invalidate(CorrectionUndoInvalidationReason.GenuineUserInput);

        public void NotifyApplicationContextChanged(string? processName, nint windowHandle)
        {
            _ = processName;
            _ = windowHandle;
            Invalidate(CorrectionUndoInvalidationReason.ApplicationContextChanged);
        }

        public void NotifyPolicyContextChanged(AutomationPolicyResult policy)
        {
            _ = policy;
            Invalidate(CorrectionUndoInvalidationReason.PolicyChanged);
        }

        public Task<CorrectionUndoResult> TryUndoAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CorrectionUndoResult.NotAvailable());
        }
    }
}
