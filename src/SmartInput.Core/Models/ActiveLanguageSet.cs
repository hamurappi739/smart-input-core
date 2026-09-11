namespace SmartInput.Core.Models;

public sealed class ActiveLanguageSet
{
    public static ActiveLanguageSet EnglishOnly { get; } = new(TypingLanguage.English);

    public static ActiveLanguageSet RussianOnly { get; } = new(TypingLanguage.Russian);

    public static ActiveLanguageSet EnglishAndRussian { get; } = new(TypingLanguage.English, TypingLanguage.Russian);

    public ActiveLanguageSet(params TypingLanguage[] languages)
    {
        ArgumentNullException.ThrowIfNull(languages);

        if (languages.Length == 0)
        {
            throw new ArgumentException("At least one language must be specified.", nameof(languages));
        }

        foreach (var language in languages)
        {
            switch (language)
            {
                case TypingLanguage.English:
                    IncludesEnglish = true;
                    break;
                case TypingLanguage.Russian:
                    IncludesRussian = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(languages), language, "Unsupported language.");
            }
        }
    }

    public bool IncludesEnglish { get; }

    public bool IncludesRussian { get; }
}
