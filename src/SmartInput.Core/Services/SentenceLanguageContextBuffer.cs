using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Services;

/// <summary>
/// Bounded in-memory language context for the last few completed tokens.
/// Stores tokens only in process memory; never exposed via status DTOs or logs.
/// </summary>
public sealed class SentenceLanguageContextBuffer
{
    public const int DefaultMinTokens = 3;
    public const int DefaultMaxTokens = 8;
    public const int DefaultMaxCharacters = 256;

    private readonly int _maxTokens;
    private readonly int _maxCharacters;
    private readonly List<string> _tokens = [];
    private bool _collectionEnabled = true;

    public SentenceLanguageContextBuffer(int maxTokens = DefaultMaxTokens, int maxCharacters = DefaultMaxCharacters)
    {
        _maxTokens = Math.Clamp(maxTokens, DefaultMinTokens, DefaultMaxTokens);
        _maxCharacters = Math.Max(32, maxCharacters);
    }

    public int TokenCount => _tokens.Count;

    public int CharacterCount => _tokens.Sum(static token => token.Length);

    public bool IsEmpty => _tokens.Count == 0;

    public void SetCollectionEnabled(bool enabled)
    {
        _collectionEnabled = enabled;
        if (!enabled)
        {
            Clear();
        }
    }

    public void Clear()
    {
        _tokens.Clear();
    }

    public void RecordCompletedToken(string token, AutomationPolicyResult? policy = null)
    {
        if (!_collectionEnabled || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (policy is not null && !AllowsCollection(policy))
        {
            Clear();
            return;
        }

        var normalized = token.Trim();
        if (normalized.Length == 0 || !normalized.All(char.IsLetter))
        {
            return;
        }

        _tokens.Add(normalized);
        TrimToBounds();
    }

    public SentenceLanguageHint GetHint()
    {
        if (_tokens.Count == 0)
        {
            return SentenceLanguageHint.Empty;
        }

        var russian = 0;
        var english = 0;
        foreach (var token in _tokens)
        {
            switch (TokenScriptAnalyzer.Classify(token))
            {
                case TokenScript.Cyrillic:
                    russian++;
                    break;
                case TokenScript.Latin:
                    english++;
                    break;
            }
        }

        var total = russian + english;
        if (total == 0)
        {
            return SentenceLanguageHint.Empty;
        }

        TypingLanguage? dominant = null;
        if (russian >= english + 2 && russian >= 2)
        {
            dominant = TypingLanguage.Russian;
        }
        else if (english >= russian + 2 && english >= 2)
        {
            dominant = TypingLanguage.English;
        }
        else if (russian > english && russian >= 3)
        {
            dominant = TypingLanguage.Russian;
        }
        else if (english > russian && english >= 3)
        {
            dominant = TypingLanguage.English;
        }

        return new SentenceLanguageHint(
            dominant,
            russian,
            english,
            _tokens.Count,
            CharacterCount);
    }

    private void TrimToBounds()
    {
        while (_tokens.Count > _maxTokens || CharacterCount > _maxCharacters)
        {
            if (_tokens.Count == 0)
            {
                break;
            }

            _tokens.RemoveAt(0);
        }
    }

    private static bool AllowsCollection(AutomationPolicyResult policy)
    {
        if (!policy.AllowsAutomation)
        {
            return false;
        }

        return policy.State == AutomationPolicyState.Allowed;
    }
}

public readonly struct SentenceLanguageHint
{
    public static SentenceLanguageHint Empty { get; } = new(null, 0, 0, 0, 0);

    public SentenceLanguageHint(
        TypingLanguage? dominantLanguage,
        int russianTokenCount,
        int englishTokenCount,
        int tokenCount,
        int characterCount)
    {
        DominantLanguage = dominantLanguage;
        RussianTokenCount = russianTokenCount;
        EnglishTokenCount = englishTokenCount;
        TokenCount = tokenCount;
        CharacterCount = characterCount;
    }

    public TypingLanguage? DominantLanguage { get; }

    public int RussianTokenCount { get; }

    public int EnglishTokenCount { get; }

    public int TokenCount { get; }

    public int CharacterCount { get; }

    public bool HasStrongRussian =>
        DominantLanguage == TypingLanguage.Russian && RussianTokenCount >= 2;

    public bool HasStrongEnglish =>
        DominantLanguage == TypingLanguage.English && EnglishTokenCount >= 2;
}
