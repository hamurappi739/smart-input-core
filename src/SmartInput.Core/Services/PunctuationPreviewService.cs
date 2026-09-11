using System.Collections.Frozen;
using System.Text;
using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

/// <summary>
/// Builds a manual, punctuation-only preview from local model proposals. This
/// service is pure: it does not read application state, write logs, call the
/// keyboard hook or apply a replacement.
/// </summary>
public sealed class PunctuationPreviewService : IPunctuationPreviewService
{
    private const int MaximumTextLength = 512;
    private const double MinimumProbability = 0.90;
    private static readonly FrozenSet<char> AllowedMarks = FrozenSet.ToFrozenSet(
        [',', '.', '?', '!', ':', ';', '…']);

    private readonly IPunctuationProvider _provider;

    public PunctuationPreviewService(IPunctuationProvider? provider = null)
    {
        _provider = provider ?? NullPunctuationProvider.Instance;
    }

    public PunctuationPreviewResult CreatePreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return PunctuationPreviewResult.NoChange;
        }

        if (text.Length > MaximumTextLength)
        {
            return new PunctuationPreviewResult { Status = PunctuationPreviewStatus.InputTooLong };
        }

        if (ContainsExistingPunctuation(text))
        {
            return new PunctuationPreviewResult { Status = PunctuationPreviewStatus.ExistingPunctuation };
        }

        if (!TryNormalizeAndTokenize(text, out var normalizedText, out var tokens))
        {
            return new PunctuationPreviewResult { Status = PunctuationPreviewStatus.UnsafeInput };
        }

        IReadOnlyList<PunctuationProposal> proposals;
        try
        {
            proposals = _provider.Analyze(normalizedText) ?? [];
        }
        catch
        {
            // A punctuation model is optional. Model failures produce no
            // change and are intentionally not reported with input text.
            return PunctuationPreviewResult.NoChange;
        }

        var selected = SelectProposals(proposals, tokens.Count);
        if (selected.Count == 0)
        {
            return PunctuationPreviewResult.NoChange;
        }

        var edits = selected
            .OrderBy(static pair => pair.Key)
            .Select(pair => new PunctuationPreviewEdit(tokens[pair.Key].End, pair.Value.Mark))
            .ToArray();
        var preview = ApplyEdits(text, edits);

        return new PunctuationPreviewResult
        {
            Status = PunctuationPreviewStatus.PreviewReady,
            OriginalText = text,
            PreviewText = preview,
            ProviderVersion = selected.Values
                .Select(static proposal => proposal.ProviderVersion)
                .FirstOrDefault(static version => !string.IsNullOrWhiteSpace(version)),
            Edits = edits,
        };
    }

    public string? RevertPreview(PunctuationPreviewResult preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return preview.Status == PunctuationPreviewStatus.PreviewReady
            && preview.Edits.Count > 0
            && !string.IsNullOrEmpty(preview.OriginalText)
            ? preview.OriginalText
            : null;
    }

    private static Dictionary<int, PunctuationProposal> SelectProposals(
        IReadOnlyList<PunctuationProposal> proposals,
        int tokenCount)
    {
        var selected = new Dictionary<int, PunctuationProposal>();
        foreach (var proposal in proposals)
        {
            if (proposal.AfterTokenIndex < 0
                || proposal.AfterTokenIndex >= tokenCount
                || !AllowedMarks.Contains(proposal.Mark)
                || double.IsNaN(proposal.Probability)
                || double.IsInfinity(proposal.Probability)
                || proposal.Probability < MinimumProbability
                || proposal.Probability > 1)
            {
                continue;
            }

            if (!selected.TryGetValue(proposal.AfterTokenIndex, out var existing)
                || proposal.Probability > existing.Probability
                || (proposal.Probability == existing.Probability && proposal.Mark < existing.Mark))
            {
                selected[proposal.AfterTokenIndex] = proposal;
            }
        }

        return selected;
    }

    private static bool TryNormalizeAndTokenize(
        string text,
        out string normalizedText,
        out List<TokenSpan> tokens)
    {
        normalizedText = string.Empty;
        tokens = [];
        if (text[0] == ' ' || text[^1] == ' ' || text.Contains("  ", StringComparison.Ordinal))
        {
            return false;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;
        var hasLatin = false;
        var hasCyrillic = false;
        while (index < text.Length)
        {
            if (text[index] == ' ')
            {
                builder.Append(' ');
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && char.IsLetter(text[index]))
            {
                index++;
            }

            if (start == index)
            {
                return false;
            }

            var token = text[start..index];
            if (IsMixedScript(token))
            {
                return false;
            }

            hasLatin |= token.Any(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
            hasCyrillic |= token.Any(static character => character is >= '\u0400' and <= '\u052F');
            if (hasLatin && hasCyrillic)
            {
                // Mixed-language text needs a language-aware model. The
                // manual preview intentionally refuses it until that model is
                // explicitly selected by the user.
                return false;
            }

            builder.Append(token);
            tokens.Add(new TokenSpan(start, index));
        }

        normalizedText = builder.ToString();
        return tokens.Count > 0;
    }

    private static bool ContainsExistingPunctuation(string text)
    {
        foreach (var character in text)
        {
            if (character is ',' or '.' or '?' or '!' or ':' or ';' or '…'
                or '@' or '/' or '\\' or '_' or '-' || char.IsDigit(character))
            {
                return true;
            }
        }

        return text.Contains("://", StringComparison.Ordinal)
            || text.Contains("www.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMixedScript(string token)
    {
        var hasLatin = false;
        var hasCyrillic = false;
        foreach (var character in token)
        {
            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                hasLatin = true;
            }
            else if (character is >= '\u0400' and <= '\u052F')
            {
                hasCyrillic = true;
            }
            else
            {
                return true;
            }
        }

        return hasLatin && hasCyrillic;
    }

    private static string ApplyEdits(string original, IReadOnlyList<PunctuationPreviewEdit> edits)
    {
        var builder = new StringBuilder(original.Length + edits.Count);
        var editIndex = 0;
        for (var index = 0; index < original.Length; index++)
        {
            builder.Append(original[index]);
            while (editIndex < edits.Count && edits[editIndex].OriginalTextIndex == index + 1)
            {
                builder.Append(edits[editIndex].Mark);
                editIndex++;
            }
        }

        return builder.ToString();
    }

    private readonly record struct TokenSpan(int Start, int End);
}
