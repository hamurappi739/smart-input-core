using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

internal enum PredictionBoundaryKind
{
    WordStart,
    MidWord,
    Ambiguous,
}

internal sealed class PredictionContextSnapshot
{
    public required IReadOnlyList<string> CompletedTokens { get; init; }

    public string? CurrentWordPrefix { get; init; }

    public PredictionBoundaryKind BoundaryKind { get; init; }

    public bool EndsWithWhitespace { get; init; }

    public bool FollowsSentenceEndingPunctuation { get; init; }

    public bool FollowsClausePunctuation { get; init; }

    public bool HasMixedLanguageTokens { get; init; }

    public bool HasProtectedToken { get; init; }
}

internal static class PredictionContextAnalyzer
{
    internal static PredictionContextSnapshot Analyze(
        string context,
        string? currentWordPrefix,
        TypingLanguage activeLanguage)
    {
        if (string.IsNullOrWhiteSpace(context) && string.IsNullOrWhiteSpace(currentWordPrefix))
        {
            return EmptySnapshot(currentWordPrefix);
        }

        var normalizedPrefix = NormalizePrefix(currentWordPrefix);
        var workingContext = context ?? string.Empty;
        var tokens = Tokenize(workingContext);
        var completedTokens = new List<string>(tokens);
        var boundaryKind = ResolveBoundaryKind(workingContext, normalizedPrefix, completedTokens);
        var followsSentenceEnding = EndsWithSentencePunctuation(workingContext);
        var followsClause = EndsWithClausePunctuation(workingContext);
        var endsWithWhitespace = workingContext.Length > 0 && char.IsWhiteSpace(workingContext[^1]);

        var hasMixedLanguage = false;
        var hasProtected = false;

        foreach (var token in completedTokens)
        {
            if (IsMixedLanguageToken(token, activeLanguage))
            {
                hasMixedLanguage = true;
            }

            if (ProtectedTokenAnalyzer.IsProtected(token))
            {
                hasProtected = true;
            }
        }

        if (!string.IsNullOrEmpty(normalizedPrefix)
            && (ProtectedTokenAnalyzer.IsProtected(normalizedPrefix) || IsMixedLanguageToken(normalizedPrefix, activeLanguage)))
        {
            hasProtected = true;
        }

        return new PredictionContextSnapshot
        {
            CompletedTokens = completedTokens,
            CurrentWordPrefix = normalizedPrefix,
            BoundaryKind = boundaryKind,
            EndsWithWhitespace = endsWithWhitespace,
            FollowsSentenceEndingPunctuation = followsSentenceEnding,
            FollowsClausePunctuation = followsClause,
            HasMixedLanguageTokens = hasMixedLanguage,
            HasProtectedToken = hasProtected,
        };
    }

    private static PredictionContextSnapshot EmptySnapshot(string? currentWordPrefix)
    {
        return new PredictionContextSnapshot
        {
            CompletedTokens = [],
            CurrentWordPrefix = NormalizePrefix(currentWordPrefix),
            BoundaryKind = string.IsNullOrEmpty(currentWordPrefix)
                ? PredictionBoundaryKind.Ambiguous
                : PredictionBoundaryKind.MidWord,
            EndsWithWhitespace = false,
            FollowsSentenceEndingPunctuation = false,
            FollowsClausePunctuation = false,
            HasMixedLanguageTokens = false,
            HasProtectedToken = !string.IsNullOrEmpty(currentWordPrefix)
                && ProtectedTokenAnalyzer.IsProtected(currentWordPrefix),
        };
    }

    private static PredictionBoundaryKind ResolveBoundaryKind(
        string context,
        string? normalizedPrefix,
        List<string> completedTokens)
    {
        if (!string.IsNullOrEmpty(normalizedPrefix))
        {
            if (completedTokens.Count > 0
                && string.Equals(
                    completedTokens[^1],
                    normalizedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                completedTokens.RemoveAt(completedTokens.Count - 1);
            }

            return PredictionBoundaryKind.MidWord;
        }

        if (context.Length == 0)
        {
            return PredictionBoundaryKind.Ambiguous;
        }

        if (char.IsWhiteSpace(context[^1]))
        {
            return PredictionBoundaryKind.WordStart;
        }

        if (char.IsPunctuation(context[^1]))
        {
            return PredictionBoundaryKind.WordStart;
        }

        if (completedTokens.Count > 0)
        {
            var trailingToken = ExtractTrailingWord(context);
            if (!string.IsNullOrEmpty(trailingToken))
            {
                completedTokens[^1] = trailingToken;
            }
        }

        return PredictionBoundaryKind.Ambiguous;
    }

    private static List<string> Tokenize(string context)
    {
        var tokens = new List<string>();
        var current = new List<char>();

        foreach (var character in context)
        {
            if (char.IsLetter(character))
            {
                current.Add(character);
                continue;
            }

            if (current.Count > 0)
            {
                tokens.Add(new string(current.ToArray()));
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            tokens.Add(new string(current.ToArray()));
        }

        return tokens;
    }

    private static string? ExtractTrailingWord(string context)
    {
        var index = context.Length - 1;
        while (index >= 0 && !char.IsLetter(context[index]))
        {
            index--;
        }

        if (index < 0)
        {
            return null;
        }

        var end = index;
        while (index >= 0 && char.IsLetter(context[index]))
        {
            index--;
        }

        return context[(index + 1)..(end + 1)];
    }

    private static bool EndsWithSentencePunctuation(string context)
    {
        var index = context.Length - 1;
        while (index >= 0 && char.IsWhiteSpace(context[index]))
        {
            index--;
        }

        return index >= 0 && context[index] is '.' or '!' or '?';
    }

    private static bool EndsWithClausePunctuation(string context)
    {
        var index = context.Length - 1;
        while (index >= 0 && char.IsWhiteSpace(context[index]))
        {
            index--;
        }

        return index >= 0 && context[index] is ',' or ';' or ':';
    }

    private static bool IsMixedLanguageToken(string token, TypingLanguage activeLanguage)
    {
        var resolved = Services.TypingLanguageResolver.Resolve(token);
        return resolved is null || resolved.Value != activeLanguage;
    }

    private static string? NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        return prefix.Trim();
    }
}
