using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

public enum EarlyLayoutVerdict { Wait, Candidate, Protected, Invalid }

// Scores are log-likelihood differences, NOT calibrated probabilities.
public readonly record struct EarlyLayoutDecision(
    EarlyLayoutVerdict Verdict, TypingLanguage TargetLanguage, double Margin, int PrefixLength);

public sealed record EarlyLayoutOptions
{
    public int MinimumLength { get; init; } = 4;
    // Calibrated for the four-key live gate: 1.25 keeps the strong RU/EN
    // prefixes actionable while the source-prefix, protected-token and
    // target-support gates remain mandatory safety checks.
    public double MinimumMargin { get; init; } = 1.25;
    // A single target word is enough at the four-character gate when the
    // source prefix is not present in its own language. Requiring two word
    // types made legitimate unique words such as "hello" wait until the
    // boundary, defeating the early-layout contract. Three-character mode
    // still has its separate >= 8 continuation guard below.
    public int MinimumTargetContinuations { get; init; } = 1;
}

/// <summary>
/// Immutable offline model over physical RU/EN key sequences. Prefix membership
/// protects unfinished source words; smoothed character transitions supply
/// independent evidence for the alternative language. No spelling candidates,
/// user text retention, dictionary enumeration or I/O during Evaluate.
/// </summary>
public sealed class EarlyLayoutModel
{
    private const string EnglishKeys = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
    private const string RussianKeys = "йцукенгшщзхъфывапролджэячсмитьбюё";
    private const int AlphabetSize = 34; // 33 physical keys plus start-of-word
    private const int Start = 33;
    public const int MaximumPrefixLength = 10;
    private const int TableSize = AlphabetSize * AlphabetSize * AlphabetSize;
    private const int Magic = 0x314C4953; // SIL1
    private static readonly EarlyLayoutOptions DefaultOptions = new();
    private readonly ulong[][] _prefixKeys;
    private readonly int[][] _prefixCounts;
    private readonly float[][] _logTransitions;

    private EarlyLayoutModel(ulong[][] keys, int[][] counts, float[][] transitions)
    {
        _prefixKeys = keys;
        _prefixCounts = counts;
        _logTransitions = transitions;
    }

    public long TableBytes => _prefixKeys.Sum(x => (long)x.Length * sizeof(ulong))
        + _prefixCounts.Sum(x => (long)x.Length * sizeof(int))
        + 2L * TableSize * sizeof(float);

    public EarlyLayoutDecision Evaluate(string token, TypingLanguage sourceLanguage,
        EarlyLayoutOptions? options = null, bool protectedContext = false)
    {
        ArgumentNullException.ThrowIfNull(token);
        options ??= DefaultOptions;
        if (options.MinimumLength is < 3 or > MaximumPrefixLength
            || !double.IsFinite(options.MinimumMargin) || options.MinimumMargin < 0
            || options.MinimumTargetContinuations < 1)
            throw new ArgumentOutOfRangeException(nameof(options));

        var target = sourceLanguage == TypingLanguage.English ? TypingLanguage.Russian : TypingLanguage.English;
        EarlyLayoutDecision Result(EarlyLayoutVerdict verdict, double margin = 0)
            => new(verdict, target, margin, token.Length);
        if (sourceLanguage is not (TypingLanguage.English or TypingLanguage.Russian))
            return Result(EarlyLayoutVerdict.Invalid);
        if (protectedContext || ProtectedTokenAnalyzer.IsProtected(token))
            return Result(EarlyLayoutVerdict.Protected);
        // Acronyms require completed-token evidence, never a mid-word guess.
        var allUpper = token.Length > 1;
        foreach (var character in token) allUpper &= char.IsUpper(character);
        if (allUpper)
            return Result(EarlyLayoutVerdict.Protected);
        if (token.Length < options.MinimumLength || token.Length > MaximumPrefixLength)
            return Result(EarlyLayoutVerdict.Wait);

        Span<int> sequence = stackalloc int[MaximumPrefixLength];
        if (!TryEncode(token, sourceLanguage, sequence, out var prefixKey))
            return Result(EarlyLayoutVerdict.Invalid);
        var source = LanguageIndex(sourceLanguage);
        var alternative = 1 - source;
        // A valid prefix is protected even if it is not a complete dictionary word.
        // This includes an acronym occurring as the start of a longer word.
        if (Array.BinarySearch(_prefixKeys[source], prefixKey) >= 0)
            return Result(EarlyLayoutVerdict.Wait);
        var targetIndex = Array.BinarySearch(_prefixKeys[alternative], prefixKey);
        if (targetIndex < 0 || _prefixCounts[alternative][targetIndex] < options.MinimumTargetContinuations)
            return Result(EarlyLayoutVerdict.Wait);

        var margin = Score(sequence[..token.Length], alternative) - Score(sequence[..token.Length], source);
        // Three-character operation needs substantially stronger support.
        var requiredMargin = options.MinimumMargin + (token.Length == 3 ? 1.5 : 0);
        var enoughSupport = token.Length != 3 || _prefixCounts[alternative][targetIndex] >= 8;
        return Result(margin >= requiredMargin && enoughSupport
            ? EarlyLayoutVerdict.Candidate : EarlyLayoutVerdict.Wait, margin);
    }

    private double Score(ReadOnlySpan<int> sequence, int language)
    {
        var previous = Start;
        var beforePrevious = Start;
        double score = 0;
        foreach (var character in sequence)
        {
            score += _logTransitions[language][(beforePrevious * AlphabetSize + previous) * AlphabetSize + character];
            beforePrevious = previous;
            previous = character;
        }
        return score / sequence.Length;
    }

    public static EarlyLayoutModel Train(IEnumerable<(TypingLanguage Language, string Word, double Weight)> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var prefixes = new[] { new Dictionary<ulong, int>(), new Dictionary<ulong, int>() };
        var triples = new[] { new double[TableSize], new double[TableSize] };
        var pairs = new[] { new double[AlphabetSize * AlphabetSize], new double[AlphabetSize * AlphabetSize] };
        var singles = new[] { new double[AlphabetSize], new double[AlphabetSize] };
        var seen = new HashSet<(TypingLanguage, string)>();
        foreach (var (language, word, weight) in words)
        {
            if (language is not (TypingLanguage.English or TypingLanguage.Russian)
                || string.IsNullOrEmpty(word) || word.Length > 128
                || !double.IsFinite(weight) || weight <= 0)
                continue;
            var normalized = word.ToLowerInvariant();
            if (!seen.Add((language, normalized))) continue;
            var sequence = new int[word.Length];
            if (!TryEncode(word, language, sequence, out _)) continue;
            var index = LanguageIndex(language);
            var previous = Start;
            var beforePrevious = Start;
            ulong key = 1;
            for (var i = 0; i < sequence.Length; i++)
            {
                var current = sequence[i];
                if (i < MaximumPrefixLength)
                {
                    key = (key << 6) | (uint)(current + 1);
                    prefixes[index][key] = prefixes[index].GetValueOrDefault(key) + 1;
                }
                triples[index][(beforePrevious * AlphabetSize + previous) * AlphabetSize + current] += weight;
                pairs[index][previous * AlphabetSize + current] += weight;
                singles[index][current] += weight;
                beforePrevious = previous;
                previous = current;
            }
        }
        var transitions = new[] { new float[TableSize], new float[TableSize] };
        for (var language = 0; language < 2; language++)
        {
            if (prefixes[language].Count == 0) throw new ArgumentException("Both language corpora are required.", nameof(words));
            var total = singles[language].Sum();
            for (var a = 0; a < AlphabetSize; a++)
            for (var b = 0; b < AlphabetSize; b++)
            {
                var tripleOffset = (a * AlphabetSize + b) * AlphabetSize;
                var tripleTotal = triples[language].AsSpan(tripleOffset, AlphabetSize).ToArray().Sum();
                var pairTotal = pairs[language].AsSpan(b * AlphabetSize, AlphabetSize).ToArray().Sum();
                for (var c = 0; c < AlphabetSize; c++)
                {
                    var unigram = (singles[language][c] + 0.1) / (total + AlphabetSize * 0.1);
                    var bigram = (pairs[language][b * AlphabetSize + c] + 2 * unigram) / (pairTotal + 2);
                    var trigram = (triples[language][tripleOffset + c] + 2 * bigram) / (tripleTotal + 2);
                    transitions[language][tripleOffset + c] = (float)Math.Log(trigram);
                }
            }
        }
        var keys = prefixes.Select(x => x.Keys.Order().ToArray()).ToArray();
        var counts = keys.Select((x, i) => x.Select(key => prefixes[i][key]).ToArray()).ToArray();
        return new EarlyLayoutModel(keys, counts, transitions);
    }

    public void Save(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        for (var language = 0; language < 2; language++)
        {
            writer.Write(_prefixKeys[language].Length);
            for (var i = 0; i < _prefixKeys[language].Length; i++)
            {
                writer.Write(_prefixKeys[language][i]);
                writer.Write(_prefixCounts[language][i]);
            }
            foreach (var value in _logTransitions[language]) writer.Write(value);
        }
    }

    public static EarlyLayoutModel Load(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Magic) throw new InvalidDataException("Unsupported early layout model.");
        var keys = new ulong[2][];
        var counts = new int[2][];
        var transitions = new float[2][];
        for (var language = 0; language < 2; language++)
        {
            var count = reader.ReadInt32();
            if (count is < 1 or > 1_000_000) throw new InvalidDataException("Invalid prefix count.");
            keys[language] = new ulong[count];
            counts[language] = new int[count];
            for (var i = 0; i < count; i++)
            {
                keys[language][i] = reader.ReadUInt64();
                counts[language][i] = reader.ReadInt32();
                if (counts[language][i] <= 0 || keys[language][i] <= 1
                    || (i > 0 && keys[language][i] <= keys[language][i - 1]))
                    throw new InvalidDataException("Invalid prefix table.");
            }
            transitions[language] = new float[TableSize];
            for (var i = 0; i < TableSize; i++)
            {
                var value = reader.ReadSingle();
                if (!float.IsFinite(value) || value > 0) throw new InvalidDataException("Invalid transition score.");
                transitions[language][i] = value;
            }
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Unexpected trailing model data.");
        return new EarlyLayoutModel(keys, counts, transitions);
    }

    private static int LanguageIndex(TypingLanguage language) => language == TypingLanguage.English ? 0 : 1;

    private static bool TryEncode(string token, TypingLanguage language, Span<int> sequence, out ulong key)
    {
        key = 1;
        var alphabet = language == TypingLanguage.English ? EnglishKeys : RussianKeys;
        for (var i = 0; i < token.Length; i++)
        {
            var character = char.ToLowerInvariant(token[i]);
            var index = alphabet.IndexOf(character);
            if (index < 0) return false;
            sequence[i] = index;
            if (i < MaximumPrefixLength) key = (key << 6) | (uint)(index + 1);
        }
        return true;
    }
}
