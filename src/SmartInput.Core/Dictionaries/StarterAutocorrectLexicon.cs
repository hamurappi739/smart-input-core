using System.Collections.Frozen;
using SmartInput.Core.Models;

namespace SmartInput.Core.Dictionaries;

/// <summary>
/// Deterministic RU/EN frequency lexicon used by the offline correction engine.
/// The embedded baseline contains roughly 50k entries per language; optional
/// Hunspell packs add full affix-based word-form validation at runtime.
/// </summary>
/// <remarks>
/// Limitations:
/// - The frequency lists are deliberately bounded so candidate ranking stays
///   fast and predictable.
/// - Hunspell packs are used for morphology and rare forms; they do not supply
///   unbounded candidates or override the safety gates.
/// </remarks>
public static class StarterAutocorrectLexicon
{
    private const string EnglishLexiconResourceName = "SmartInput.Core.Dictionaries.Lexicons.en_50k.txt";
    private const string RussianLexiconResourceName = "SmartInput.Core.Dictionaries.Lexicons.ru_50k.csv";
    private const int EnglishLexiconSourceCount = 50_000;
    private const int RussianLexiconSourceCount = 50_000;

    public static IReadOnlyDictionary<(TypingLanguage Language, string Word), double> Entries { get; } =
        BuildEntries();

    public static int EnglishWordCount { get; }

    public static int RussianWordCount { get; }

    static StarterAutocorrectLexicon()
    {
        EnglishWordCount = Entries.Count(pair => pair.Key.Language == TypingLanguage.English);
        RussianWordCount = Entries.Count(pair => pair.Key.Language == TypingLanguage.Russian);
    }

    private static FrozenDictionary<(TypingLanguage Language, string Word), double> BuildEntries()
    {
        var entries = new Dictionary<(TypingLanguage, string), double>();

        AddEnglish(entries,
        [
            ("hello", 1.00),
            ("world", 0.90),
            ("the", 1.00),
            ("test", 0.80),
            ("word", 0.82),
            ("this", 0.95),
            ("that", 0.95),
            ("with", 0.95),
            ("from", 0.92),
            ("have", 0.94),
            ("been", 0.88),
            ("were", 0.88),
            ("what", 0.93),
            ("when", 0.90),
            ("where", 0.90),
            ("which", 0.88),
            ("while", 0.82),
            ("would", 0.90),
            ("could", 0.88),
            ("should", 0.88),
            ("about", 0.92),
            ("after", 0.86),
            ("before", 0.86),
            ("during", 0.80),
            ("under", 0.82),
            ("over", 0.84),
            ("through", 0.78),
            ("between", 0.76),
            ("without", 0.74),
            ("english", 0.70),
            ("russian", 0.70),
            ("local", 0.72),
            ("input", 0.72),
            ("text", 0.80),
            ("typing", 0.75),
            ("keyboard", 0.70),
            ("correct", 0.78),
            ("correction", 0.72),
             ("language", 0.82),
             ("api", 0.86),
             // Compact supplemental vocabulary for common technical words
             // and compounds that are often absent from general-purpose
             // frequency lists but are still valid layout targets.
             ("frontend", 0.86),
             ("backend", 0.86),
             ("fullstack", 0.84),
             ("database", 0.86),
             ("middleware", 0.82),
             ("software", 0.86),
             ("hardware", 0.86),
             ("websocket", 0.82),
             ("javascript", 0.84),
             ("typescript", 0.84),
             ("docker", 0.84),
             ("kubernetes", 0.82),
             ("github", 0.84),
             ("telegram", 0.84),
             ("localhost", 0.84),
             ("request", 0.86),
             ("response", 0.86),
             ("server", 0.88),
             ("client", 0.88),
             ("runtime", 0.84),
             ("thread", 0.84),
             ("process", 0.84),
             ("memory", 0.84),
             ("latency", 0.82),
             ("benchmark", 0.82),
             ("morphology", 0.80),
             ("dictionary", 0.84),
             ("context", 0.86),
             ("security", 0.86),
             ("privacy", 0.86),
             ("preview", 0.82),
             ("allowlist", 0.80),
         ]);

        AddRussian(entries,
        [
            ("привет", 1.00),
            ("здравствуйте", 0.99),
            ("спасибо", 0.99),
            ("пожалуйста", 0.99),
            ("команда", 0.99),
            ("мир", 0.90),
            ("это", 0.95),
            ("тот", 0.90),
            ("как", 0.94),
            ("что", 0.96),
            ("когда", 0.90),
            ("где", 0.90),
            ("который", 0.82),
            ("пока", 0.84),
            ("было", 0.88),
            ("были", 0.86),
            ("есть", 0.94),
            ("был", 0.90),
            ("была", 0.88),
            // High-frequency pronouns and case forms. Keep these explicit
            // because the bundled corpus does not contain every common
            // inflected form; a missing valid word can otherwise be replaced
            // by a rarer one-edit dictionary candidate (for example
            // "меня" -> "сеня").
            ("меня", 0.99),
            ("мне", 0.99),
            ("тебя", 0.98),
            ("тебе", 0.98),
            ("себе", 0.98),
            ("нам", 0.98),
            ("вам", 0.98),
            ("им", 0.98),
            ("ей", 0.98),
            ("ему", 0.98),
            ("ими", 0.97),
            // Common colloquial and profane vocabulary is still valid user
            // language. Treat it as known so layout detection and spelling
            // correction do not turn it into an English-looking candidate.
            ("гавно", 0.99),
            ("говно", 0.99),
            ("говнюк", 0.95),
            ("дерьмо", 0.98),
            ("дерьмовый", 0.92),
            ("блядь", 0.99),
            ("блять", 0.99),
            ("бля", 0.99),
            ("сука", 0.99),
            ("сучка", 0.96),
            ("хуй", 0.99),
            ("хуйню", 0.96),
            ("хуйней", 0.96),
            ("хуёвый", 0.94),
            ("хуевый", 0.94),
            ("пизда", 0.99),
            ("пиздец", 0.99),
            ("пиздато", 0.95),
            ("ебать", 0.99),
            ("ебаный", 0.98),
            ("ёбаный", 0.98),
            ("ебанный", 0.96),
            ("еблан", 0.96),
            ("долбоеб", 0.96),
            ("долбоёб", 0.96),
            ("мудак", 0.98),
            ("мудацкий", 0.92),
            ("мудила", 0.94),
            ("козёл", 0.90),
            ("козел", 0.90),
            ("чмо", 0.96),
            ("тварь", 0.94),
            ("шлюха", 0.95),
            ("проститутка", 0.90),
            ("нахуй", 0.98),
            ("нахуя", 0.98),
            ("похуй", 0.98),
            ("заебал", 0.98),
            ("заебись", 0.98),
            ("заебало", 0.96),
            ("уёбок", 0.96),
            ("уебок", 0.96),
            ("уебать", 0.94),
            ("ебусь", 0.94),
            ("ебёт", 0.94),
            ("ебет", 0.94),
            ("ебу", 0.94),
            ("ебёшь", 0.92),
            ("ебешь", 0.92),
            ("ебучий", 0.94),
            ("ебучая", 0.94),
            ("ебучее", 0.94),
            ("ебучие", 0.94),
            ("сраный", 0.96),
            ("сраная", 0.96),
            ("срать", 0.98),
            ("насрать", 0.96),
            ("обосрался", 0.92),
            ("обосраться", 0.92),
            ("жопа", 0.99),
            ("жопу", 0.96),
            ("жопой", 0.94),
            ("задница", 0.94),
            ("писец", 0.90),
            ("писюнь", 0.88),
            ("млять", 0.92),
            ("мразь", 0.96),
            ("скотина", 0.94),
            ("идиот", 0.90),
            ("дебил", 0.94),
            ("дебильный", 0.90),
            ("придурок", 0.92),
            ("урод", 0.92),
            ("уродина", 0.92),
            ("мерзавец", 0.88),
            ("будут", 0.99),
            ("начало", 0.99),
            ("начала", 0.99),
            ("началу", 0.97),
            ("началом", 0.96),
            ("начале", 0.97),
            ("начал", 0.98),
            ("начали", 0.98),
            ("начать", 0.98),
            ("начинаю", 0.96),
            ("начинаешь", 0.95),
            ("начинает", 0.97),
            ("начинаем", 0.95),
            ("начинаете", 0.94),
            ("начинают", 0.96),
            ("начинал", 0.95),
            ("начинала", 0.95),
            ("начинали", 0.95),
            ("начиная", 0.96),
            ("начав", 0.94),
            ("началась", 0.96),
            ("дать", 0.99),
            ("дам", 0.98),
            ("дашь", 0.97),
            ("даст", 0.98),
            ("дадим", 0.96),
            ("дадите", 0.95),
            ("дадут", 0.96),
            ("дала", 0.98),
            ("дало", 0.97),
            ("дали", 0.98),
            ("давать", 0.98),
            ("даю", 0.97),
            ("даешь", 0.97),
            ("даёшь", 0.97),
            ("дает", 0.99),
            ("даёт", 0.99),
            ("даем", 0.96),
            ("даём", 0.96),
            ("даете", 0.95),
            ("даёте", 0.95),
            ("дают", 0.98),
            ("давал", 0.96),
            ("давала", 0.95),
            ("давали", 0.95),
            ("давай", 0.98),
            ("давайте", 0.97),
            // Common command/request forms used in everyday text. These are
            // also important layout targets (for example lfq -> дай) and
            // must be recognized as valid Russian words rather than falling
            // into the ambiguous English-spelling path.
            ("дай", 0.99),
            ("команду", 0.98),
            ("запуска", 0.96),
            ("жать", 0.90),
            ("стать", 0.98),
            ("стала", 0.98),
            ("стало", 0.98),
            ("знать", 0.99),
            ("пить", 0.97),
            ("жить", 0.98),
            ("живет", 0.97),
            ("живёт", 0.97),
            ("живем", 0.95),
            ("живём", 0.95),
            ("бить", 0.96),
            ("быть", 0.99),
            ("том", 0.95),
            ("дом", 0.98),
            ("код", 0.96),
            ("кот", 0.96),
            ("неизвестный", 0.97),
            ("неизвестная", 0.96),
            ("неизвестное", 0.97),
            ("неизвестные", 0.96),
            ("неизвестного", 0.95),
            ("неизвестной", 0.95),
            ("самого", 0.99),
            ("неизвестному", 0.94),
            ("неизвестным", 0.94),
            ("неизвестно", 0.98),
            ("делу", 0.97),
            ("делом", 0.97),
            ("деле", 0.96),
            ("дел", 0.95),
            ("делами", 0.94),
            ("делах", 0.94),
            ("слово", 0.99),
            ("молоко", 0.99),
            ("голова", 0.98),
            ("собака", 0.98),
            ("интерфейс", 0.92),
            ("ребенок", 0.98),
            ("слова", 0.99),
            ("слову", 0.96),
            ("словом", 0.95),
            ("слове", 0.95),
            ("слов", 0.97),
            ("словами", 0.94),
            ("словах", 0.94),
            ("место", 0.98),
            ("места", 0.98),
            ("месту", 0.95),
            ("местом", 0.94),
            ("месте", 0.95),
            ("мест", 0.96),
            ("видеть", 0.98),
            ("вижу", 0.98),
            ("видишь", 0.97),
            ("видит", 0.98),
            ("видим", 0.96),
            ("видите", 0.95),
            ("видят", 0.96),
            ("видела", 0.97),
            ("видело", 0.95),
            ("видели", 0.97),
            ("писать", 0.98),
            ("пишешь", 0.96),
            ("пишет", 0.97),
            ("пишем", 0.95),
            ("пишете", 0.94),
            ("пишут", 0.96),
            ("писал", 0.97),
            ("писала", 0.96),
            ("писало", 0.94),
            ("писали", 0.96),
            ("менять", 0.98),
            ("меняю", 0.96),
            ("меняешь", 0.95),
            ("меняет", 0.96),
            ("меняем", 0.94),
            ("меняете", 0.93),
            ("меняют", 0.95),
            ("менял", 0.95),
            ("меняла", 0.94),
            ("меняли", 0.94),
            ("меняйте", 0.95),
            ("мы", 0.99),
            ("нами", 0.97),
            ("я", 0.99),
            ("мной", 0.97),
            ("мною", 0.94),
            ("все", 0.99),
            ("всё", 0.99),
            ("еще", 0.99),
            ("ещё", 0.99),
            ("ее", 0.97),
            ("её", 0.97),
            ("идет", 0.98),
            ("идёт", 0.98),
            ("ждет", 0.97),
            ("ждёт", 0.97),
            ("берет", 0.96),
            ("берёт", 0.96),
            ("пойдет", 0.95),
            ("пойдёт", 0.95),
            ("придет", 0.95),
            ("придёт", 0.95),
            ("найдет", 0.94),
            ("найдёт", 0.94),
            ("поет", 0.94),
            ("поёт", 0.94),
            ("серьезный", 0.95),
            ("серьёзный", 0.95),
            ("дал", 0.99),
            ("видел", 0.99),
            ("видик", 0.35),
            ("дела", 0.99),
            ("дело", 0.99),
            ("нас", 0.99),
            ("делай", 0.99),
            ("машина", 0.99),
            ("меняй", 0.99),
            ("дерись", 0.98),
            ("пенис", 0.96),
            ("написал", 0.99),
            ("исследование", 0.98),
            // Common inflected forms that are absent from the compact corpus
            // but are frequent enough to be safe spelling targets.
            ("решения", 0.99),
            ("пояснить", 0.98),
            ("специально", 0.99),
            ("исправляет", 0.98),
            ("обстоят", 0.96),
            ("дополнение", 0.96),
            ("душ", 0.99),
            ("пишу", 0.99),
            ("лал", 0.20),
            ("сеня", 0.20),
            ("касса", 0.98),
            ("ванна", 0.98),
            ("группа", 0.98),
            ("класс", 0.98),
            ("суббота", 0.98),
            ("россия", 0.98),
            ("алла", 0.95),
            ("анна", 0.95),
            ("тонна", 0.94),
            ("сумма", 0.95),
            ("комиссия", 0.94),
            ("профессия", 0.94),
            ("территория", 0.93),
            ("искусство", 0.94),
            ("рассказ", 0.95),
            ("программа", 0.96),
            ("помощь", 0.85),
            ("тест", 0.80),
            ("после", 0.86),
            ("перед", 0.84),
            ("между", 0.76),
            ("через", 0.78),
            ("без", 0.88),
            ("english", 0.70),
            ("russian", 0.70),
            ("локальный", 0.72),
            ("ввод", 0.72),
            ("текст", 0.80),
            ("набор", 0.75),
            ("клавиатура", 0.70),
            ("исправление", 0.72),
            ("язык", 0.82),
            ("словарь", 0.78),
            ("пользователь", 0.74),
            ("настройка", 0.72),
            ("защита", 0.70),
            ("автомат", 0.68),
            ("проверка", 0.70),
            ("верный", 0.68),
            ("ошибка", 0.76),
        ]);

        AddEmbeddedEnglishLexicon(entries);
        AddEmbeddedRussianLexicon(entries);

        return entries.ToFrozenDictionary();
    }

    private static FrozenSet<string> CreateEnglishCorpusNoiseTypos()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "helo",
            "helllo",
            "teh",
            "hte",
            "adn",
            "nad",
            "recieve",
            "becuase",
            "thier",
            "taht",
            "wiht",
            "ahve",
            "jsut",
            "lenght",
            "seperate",
            "occured",
            "untill",
            "wich",
            "wihch",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenSet<string> CreateRussianCorpusNoiseTypos()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "превет",
            "превед",
            "спасиба",
            "пажалуйста",
            "здраствуйте",
            "извени",
            "пожалуста",
            "мыш",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddEmbeddedEnglishLexicon(Dictionary<(TypingLanguage, string), double> entries)
    {
        AddEmbeddedWords(
            entries,
            TypingLanguage.English,
            EnglishLexiconResourceName,
            EnglishLexiconSourceCount,
            static line =>
            {
                var separator = line.IndexOf(' ');
                return separator <= 0 ? null : line[..separator];
            },
            CreateEnglishCorpusNoiseTypos());
    }

    private static void AddEmbeddedRussianLexicon(Dictionary<(TypingLanguage, string), double> entries)
    {
        AddEmbeddedWords(
            entries,
            TypingLanguage.Russian,
            RussianLexiconResourceName,
            RussianLexiconSourceCount,
            static line =>
            {
                var separator = line.IndexOf(',');
                return separator <= 0 ? null : line[..separator];
            },
            CreateRussianCorpusNoiseTypos(),
            skipFirstLine: true);
    }

    private static void AddEmbeddedWords(
        Dictionary<(TypingLanguage, string), double> entries,
        TypingLanguage language,
        string resourceName,
        int expectedSourceCount,
        Func<string, string?> getWord,
        FrozenSet<string> noiseTypos,
        bool skipFirstLine = false)
    {
        using var stream = typeof(StarterAutocorrectLexicon).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded lexicon resource was not found: {resourceName}");
        using var reader = new StreamReader(stream);

        if (skipFirstLine)
        {
            _ = reader.ReadLine();
        }

        var rank = 0;
        while (reader.ReadLine() is { } line)
        {
            rank++;
            var word = getWord(line);
            if (word is null || !IsWordForLanguage(word, language) || noiseTypos.Contains(word))
            {
                continue;
            }

            var key = (language, AutocorrectDictionaryNormalizer.NormalizeLookupKey(word));
            entries.TryAdd(key, GetRankFrequency(rank, expectedSourceCount));
        }
    }

    private static double GetRankFrequency(int rank, int sourceCount)
    {
        // The files are sorted from more to less frequent. The deliberately
        // modest floor keeps rare words available for protection while giving
        // high-confidence automatic-layout evidence to everyday vocabulary.
        var normalizedRank = (double)(rank - 1) / Math.Max(1, sourceCount - 1);
        return Math.Clamp(1.0 - (normalizedRank * 0.65), 0.35, 1.0);
    }

    private static bool IsWordForLanguage(string word, TypingLanguage language)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        foreach (var character in word)
        {
            var isExpectedLetter = language switch
            {
                TypingLanguage.English => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z',
                TypingLanguage.Russian => character is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё',
                _ => false,
            };

            if (!isExpectedLetter)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddEnglish(
        Dictionary<(TypingLanguage, string), double> entries,
        IEnumerable<(string Word, double Frequency)> words)
    {
        foreach (var (word, frequency) in words)
        {
            entries[(TypingLanguage.English, AutocorrectDictionaryNormalizer.NormalizeLookupKey(word))] = frequency;
        }
    }

    private static void AddRussian(
        Dictionary<(TypingLanguage, string), double> entries,
        IEnumerable<(string Word, double Frequency)> words)
    {
        foreach (var (word, frequency) in words)
        {
            entries[(TypingLanguage.Russian, AutocorrectDictionaryNormalizer.NormalizeLookupKey(word))] = frequency;
        }
    }
}
