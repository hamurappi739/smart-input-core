using System.Collections.Frozen;

namespace SmartInput.Core.Services;

/// <summary>
/// Small deterministic offline baseline for the manual punctuation preview.
/// It proposes only a sentence-ending mark and a narrow set of conjunction
/// boundaries. It never edits text and is never called by the keyboard hook.
/// A statistical/ONNX provider can replace it later behind the same contract.
/// </summary>
public sealed class RuleBasedPunctuationProvider : IPunctuationProvider
{
    private static readonly FrozenSet<string> RussianQuestionStarts =
        new[] { "кто", "что", "где", "куда", "откуда", "когда", "почему", "зачем", "как", "сколько", "какой", "какая", "какие", "можно", "нужно", "разве", "неужели" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> EnglishQuestionStarts =
        new[] { "who", "what", "where", "when", "why", "how", "which", "can", "could", "would", "should", "is", "are", "do", "does", "did" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CommaBeforeWords =
        new[]
        {
            "а", "но", "однако", "зато", "если", "когда", "хотя", "чтобы", "потому", "который", "которая", "которые", "которое", "что",
            "but", "however", "although", "if", "when", "because", "which", "that",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SmartInput.Core.Models.PunctuationProposal> Analyze(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return [];
        }

        var tokens = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return [];
        }

        var result = new List<SmartInput.Core.Models.PunctuationProposal>(capacity: 4);
        var first = TrimApostrophe(tokens[0]);
        var isQuestion = RussianQuestionStarts.Contains(first) || EnglishQuestionStarts.Contains(first);
        result.Add(new SmartInput.Core.Models.PunctuationProposal(
            tokens.Length - 1,
            isQuestion ? '?' : '.',
            isQuestion ? 0.94 : 0.92,
            "rule-baseline-1"));

        for (var index = 1; index < tokens.Length; index++)
        {
            var current = TrimApostrophe(tokens[index]);
            var previous = TrimApostrophe(tokens[index - 1]);
            if (CommaBeforeWords.Contains(current)
                && previous.Length > 1
                && !CommaBeforeWords.Contains(previous))
            {
                result.Add(new SmartInput.Core.Models.PunctuationProposal(
                    index - 1,
                    ',',
                    0.92,
                    "rule-baseline-1"));
            }
        }

        return result;
    }

    private static string TrimApostrophe(string token)
        => token.Trim('\'', '’', '"').ToLowerInvariant();
}
