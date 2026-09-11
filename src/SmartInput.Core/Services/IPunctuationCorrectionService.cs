using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

/// <summary>
/// Pure punctuation-spacing correction. Caller supplies recent text ending at the caret;
/// no keyboard or global state is read.
/// </summary>
public interface IPunctuationCorrectionService
{
    PunctuationCorrectionResult Evaluate(string textBeforePunctuation, char punctuationCharacter);
}
