using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Engines;

/// <summary>
/// Final Apply gate: delegates to bounded candidate-set safety invariant.
/// </summary>
internal static class ConfidentCorrectionApplyGate
{
    internal static bool AllowsApply(
        string token,
        string replacement,
        CorrectionKind kind,
        TypingLanguage? sourceLanguage,
        IAutocorrectDictionary dictionary,
        ILayoutConversionService layoutConverter,
        AutocorrectionOptions options,
        SentenceLanguageHint? languageHint = null)
    {
        var allowed = BoundedCandidateApplyGuard.AllowsApply(
            token,
            replacement,
            kind,
            sourceLanguage,
            dictionary,
            layoutConverter,
            options,
            languageHint);
        ApplyGateTelemetry.RecordCall(allowed);
        return allowed;
    }
}
