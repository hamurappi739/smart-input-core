using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Integration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Engines;

/// <summary>
/// Conservative bridge from the optional Rust scorer into the existing live
/// decision pipeline. Rust can propose a candidate, but it cannot replace
/// text: the normal Core apply gate remains the final authority.
/// </summary>
public interface IRustHybridCorrectionService
{
    bool IsLiveEnabled { get; }

    JointCorrectionDecisionResult? TryCreateApprovedDecision(
        string token,
        JointCorrectionDecisionResult coreDecision,
        IAutocorrectDictionary dictionary,
        bool layoutEnabled,
        bool autocorrectEnabled,
        AutocorrectionOptions options,
        SentenceLanguageHint languageHint);
}

public sealed class RustHybridCorrectionService : IRustHybridCorrectionService
{
    public const string LiveModeEnvironmentVariable = "SMARTINPUT_RUST_LIVE_MODE";
    public const string AllowListMode = "allow-list";

    // Match the already-established Rust promotion bar. This prevents an
    // optional provider from becoming an aggressive second autocorrect engine.
    public const double MinimumConfidence = 0.98;
    public const double MinimumMargin = 0.20;

    private readonly IRustShadowCandidateProvider _provider;
    private readonly ILayoutConversionService _layoutConverter;
    private readonly bool _liveEnabled;

    public RustHybridCorrectionService(
        IRustShadowCandidateProvider provider,
        ILayoutConversionService? layoutConverter = null,
        bool liveEnabled = true)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _layoutConverter = layoutConverter ?? new KeyboardLayoutConverter();
        _liveEnabled = liveEnabled;
    }

    public bool IsLiveEnabled => _liveEnabled;

    public static bool IsAllowListModeEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable(LiveModeEnvironmentVariable),
            AllowListMode,
            StringComparison.OrdinalIgnoreCase);

    public JointCorrectionDecisionResult? TryCreateApprovedDecision(
        string token,
        JointCorrectionDecisionResult coreDecision,
        IAutocorrectDictionary dictionary,
        bool layoutEnabled,
        bool autocorrectEnabled,
        AutocorrectionOptions options,
        SentenceLanguageHint languageHint)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(coreDecision);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(languageHint);

        if (!_liveEnabled)
        {
            return null;
        }

        // Never override an already approved Core result. Rust is an
        // additional provider, not a tie-breaker for the trusted engine.
        if (coreDecision.Recommendation == JointCorrectionRecommendation.Apply)
        {
            return null;
        }

        // A NoChange result is a deliberate Core conclusion (for example an
        // exact known word or a protected semantic case). Rust may supplement
        // only an unresolved Wait, never reinterpret that conclusion.
        if (coreDecision.Recommendation != JointCorrectionRecommendation.Wait)
        {
            return null;
        }

        if (_provider.State != RustShadowProviderState.Available)
        {
            return null;
        }

        var context = RustShadowContextFormatter.FromHint(languageHint);
        RustShadowCandidateResult rust;
        try
        {
            rust = _provider.Evaluate(token, context);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or ObjectDisposedException
                or System.Runtime.InteropServices.SEHException)
        {
            return null;
        }

        if (rust.ProviderState != RustShadowProviderState.Available
            || rust.Decision != RustShadowDecision.Replace
            || string.IsNullOrWhiteSpace(rust.ReplacementToken)
            || string.Equals(token, rust.ReplacementToken, StringComparison.OrdinalIgnoreCase)
            || !IsSafeSingleToken(rust.ReplacementToken)
            || !double.IsFinite(rust.Confidence)
            || rust.Confidence < MinimumConfidence
            || !double.IsFinite(rust.Margin)
            || rust.Margin < MinimumMargin)
        {
            return null;
        }

        var kind = rust.Reason switch
        {
            RustShadowReason.Layout => CorrectionKind.Layout,
            RustShadowReason.Spelling or RustShadowReason.LearnedCorrection => CorrectionKind.Autocorrect,
            _ => (CorrectionKind?)null,
        };

        if (kind is null
            || kind == CorrectionKind.Layout && !layoutEnabled
            || kind == CorrectionKind.Autocorrect && !autocorrectEnabled)
        {
            return null;
        }

        var sourceLanguage = TryGetSourceLanguage(token);
        if (sourceLanguage is null)
        {
            return null;
        }

        // Rust spelling is supplemental only. Unlike layout conversion, it
        // has no deterministic keyboard mapping and its compact score does
        // not carry the C# morphology/corpus provenance. Do not promote a
        // low-frequency spelling target into live replacement; such a target
        // is still useful in shadow/audit mode. Core remains free to apply
        // its own already-approved spelling result before this bridge.
        if (kind == CorrectionKind.Autocorrect
            && dictionary.GetFrequency(rust.ReplacementToken, sourceLanguage.Value) < 0.70)
        {
            return null;
        }

        if (!ConfidentCorrectionApplyGate.AllowsApply(
                token,
                rust.ReplacementToken,
                kind.Value,
                sourceLanguage,
                dictionary,
                _layoutConverter,
                options,
                languageHint))
        {
            return null;
        }

        var targetLanguage = kind == CorrectionKind.Layout
            ? sourceLanguage == TypingLanguage.Russian
                ? KeyboardInputLanguage.English
                : KeyboardInputLanguage.Russian
            : (KeyboardInputLanguage?)null;

        return new JointCorrectionDecisionResult
        {
            OriginalToken = token,
            ReplacementToken = rust.ReplacementToken,
            Recommendation = JointCorrectionRecommendation.Apply,
            Kind = kind.Value,
            ConfidenceScore = rust.Confidence,
            LayoutDirection = kind == CorrectionKind.Layout
                ? sourceLanguage == TypingLanguage.Russian
                    ? LayoutConversionDirection.RussianToEnglish
                    : LayoutConversionDirection.EnglishToRussian
                : null,
            TargetInputLanguage = targetLanguage,
        };
    }

    private static bool IsSafeSingleToken(string candidate)
    {
        if (candidate.Length > 256 || candidate.Any(char.IsWhiteSpace))
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    private static TypingLanguage? TryGetSourceLanguage(string token) =>
        TokenScriptAnalyzer.Classify(token) switch
        {
            TokenScript.Cyrillic => TypingLanguage.Russian,
            TokenScript.Latin => TypingLanguage.English,
            _ => null,
        };
}
