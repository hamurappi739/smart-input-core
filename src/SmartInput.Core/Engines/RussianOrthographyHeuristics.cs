using SmartInput.Core.Models;
using SmartInput.Core.Dictionaries;

namespace SmartInput.Core.Engines;

internal static class RussianOrthographyHeuristics
{
    internal static bool IsLikelyTerminalSoftSignOmission(
        string source,
        TypingLanguage language)
    {
        return language == TypingLanguage.Russian
            && source.Length >= 3
            && (source.EndsWith("еш", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("ост", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("ност", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsTerminalSoftSignCompletion(
        string source,
        string candidate,
        TypingLanguage language)
    {
        return language == TypingLanguage.Russian
            && source.Length + 1 == candidate.Length
            && IsLikelyTerminalSoftSignOmission(source, language)
            && candidate.EndsWith('ь')
            && candidate.StartsWith(source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a correction only for a narrow, deterministic Russian spelling
    /// pattern. The candidate still has to be present in the runtime
    /// dictionary; this method never invents a word or overrides an exact
    /// known token.
    /// </summary>
    internal static bool TryGetDeterministicCorrection(
        string source,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        out string correction)
    {
        correction = string.Empty;
        if (language != TypingLanguage.Russian
            || source.Length < 3
            || ShouldPreserveExactWord(source, language, dictionary))
        {
            return false;
        }

        var hardCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDigraphCorrections(source, hardCandidates);
        if (TrySelectUniqueDictionaryCandidate(hardCandidates, dictionary, language, out correction))
        {
            return true;
        }

        // Do not let later vowel/shape logic bypass a malformed жи/ши/ча/
        // ща/чу/щу sequence and select an unrelated dictionary neighbour.
        if (HasForbiddenRussianDigraph(source))
        {
            return false;
        }

        // Handle narrow suffix omissions before the broader structural search
        // so a shorter dictionary word cannot win the tie.
        var suffixCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddMissingIBeforeTerminalE(source, suffixCandidates);
        AddMissingEBeforeNSequence(source, suffixCandidates);
        AddMissingTerminalE(source, suffixCandidates);
        AddMissingYBeforeTerminalS(source, suffixCandidates);
        if (source.EndsWith("тть", StringComparison.OrdinalIgnoreCase))
        {
            suffixCandidates.Add(source[..^3] + "тать");
        }
        if (TrySelectUniqueDictionaryCandidate(suffixCandidates, dictionary, language, out correction))
        {
            return true;
        }

        if (TryGetDeterministicVowelCorrection(source, language, dictionary, out correction))
        {
            return true;
        }

        // Repeated-letter errors are high precision. Prefer the common
        // two-signal form (collapse a duplicate and restore a terminal vowel)
        // before accepting the shorter valid word produced by just collapsing.
        var repeatedCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRepeatedCharacterCleanup(source, repeatedCandidates);
        var repeatedWithTerminalVowel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in repeatedCandidates)
        {
            for (var index = 0; index < source.Length - 1; index++)
            {
                if (source[index] == source[index + 1])
                {
                    repeatedWithTerminalVowel.Add(candidate + source[index]);
                }
            }
        }

        if (TrySelectBestRepeatedCandidate(
                repeatedCandidates.Concat(repeatedWithTerminalVowel),
                dictionary,
                language,
                out correction))
        {
            return true;
        }

        var insertionCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSingleCharacterInsertions(source, insertionCandidates);
        if (TrySelectHighConfidenceInsertionCandidate(source, insertionCandidates, dictionary, language, out correction))
        {
            return true;
        }

        // Adjacent transpositions should not be outranked by an unrelated
        // insertion candidate from a large dictionary.
        var transpositionCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddAdjacentTranspositions(source, transpositionCandidates);
        if (source.Length > 3
            && TrySelectUniqueDictionaryCandidate(transpositionCandidates, dictionary, language, out correction))
        {
            return true;
        }

        // Double-consonant and soft-sign morphology often requires two small
        // structural edits (интелектуалный→интеллектуальный). Explore only
        // these bounded orthographic transforms, never arbitrary edit paths.
        var structuralCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddStructuralCorrections(source, structuralCandidates, maxDepth: 2);
        var doubleAndSoftSignCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDoubleAndSoftSignCorrections(source, doubleAndSoftSignCandidates);
        if (TrySelectUniqueDictionaryCandidate(doubleAndSoftSignCandidates, dictionary, language, out correction))
        {
            return true;
        }

        if (TrySelectDoubleAndSoftSignCandidate(source, structuralCandidates, dictionary, language, out correction))
        {
            return true;
        }

        if (TrySelectStructuralCandidate(source, structuralCandidates, dictionary, language, out correction))
        {
            return true;
        }

        // The spelling provider may return only its first few suggestions.
        // Rebuild the bounded one-edit universe locally for likely missing
        // characters and adjacent transpositions, then accept only one
        // dictionary-backed result.
        var shapeCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSingleCharacterInsertions(source, shapeCandidates);
        AddAdjacentTranspositions(source, shapeCandidates);
        AddRepeatedCharacterCleanup(source, shapeCandidates);
        if (TrySelectInsertionCandidate(source, shapeCandidates, dictionary, language, out correction))
        {
            return true;
        }

        if (TrySelectStrongCandidate(shapeCandidates, dictionary, language, out correction))
        {
            return true;
        }

        return false;
    }

    internal static bool TryGetLowFrequencyTerminalSoftSignCorrection(
        string source,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        out string correction)
    {
        correction = string.Empty;
        if (language != TypingLanguage.Russian || source.Length < 3 || source.EndsWith('ь'))
        {
            return false;
        }

        var candidate = source + 'ь';
        if (!dictionary.Contains(candidate, language))
        {
            return false;
        }

        var sourceFrequency = dictionary.GetFrequency(source, language);
        var candidateFrequency = dictionary.GetFrequency(candidate, language);
        if (candidateFrequency < 0.68
            || (candidateFrequency < sourceFrequency + 0.10 && sourceFrequency > 0.68))
        {
            return false;
        }

        correction = candidate;
        return true;
    }

    internal static bool IsLowFrequencyRussianSpellingProbe(
        string source,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        if (language != TypingLanguage.Russian
            || source.Length < 4
            || dictionary.GetFrequency(source, language) > 0.70)
        {
            return false;
        }

        if (source.Any(character => "аеёиоуыэюя".Contains(character, StringComparison.OrdinalIgnoreCase)))
        {
            var vowelCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddVowelCorrections(source, vowelCandidates, maxDepth: 2);
            if (vowelCandidates.Any(candidate =>
                    dictionary.Contains(candidate, language)
                    && dictionary.GetFrequency(candidate, language) >= 0.95))
            {
                return true;
            }
        }

        const string russianAlphabet = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        for (var index = 0; index < source.Length; index++)
        {
            foreach (var character in russianAlphabet)
            {
                if (character == source[index])
                {
                    continue;
                }

                var candidate = source.ToCharArray();
                candidate[index] = character;
                var replacement = new string(candidate);
                if (dictionary.Contains(replacement, language)
                    && dictionary.GetFrequency(replacement, language) >= 0.95)
                {
                    return true;
                }
            }
        }

        var repeatedCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRepeatedCharacterCleanup(source, repeatedCandidates);
        if (repeatedCandidates.Any(candidate =>
            dictionary.Contains(candidate, language)
            && dictionary.GetFrequency(candidate, language) >= 0.90))
        {
            return true;
        }

        // Hunspell can accept a malformed single-consonant form as a
        // morphology-derived word. A unique, dictionary-backed insertion of a
        // doubled consonant is stronger evidence of a spelling omission than
        // that permissive morphology result, so surface it to the caller
        // before the exact-word preservation gate wins.
        return HasHighSignalDoubleConsonantInsertion(source, language, dictionary);
    }

    private static bool HasHighSignalDoubleConsonantInsertion(
        string source,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        if (language != TypingLanguage.Russian)
        {
            return false;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDoubleConsonantCorrections(source, candidates);
        var valid = candidates
            .Where(candidate => dictionary.Contains(candidate, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(candidate => dictionary.GetFrequency(candidate, language))
            .ToArray();
        if (valid.Length == 0)
        {
            return false;
        }

        var best = dictionary.GetFrequency(valid[0], language);
        if (valid.Length == 1)
        {
            return best >= 0.68;
        }

        var second = dictionary.GetFrequency(valid[1], language);
        return best >= 0.85 && best >= second + 0.05;
    }

    internal static bool ShouldPreserveExactWord(
        string source,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        if (!dictionary.Contains(source, language))
        {
            return false;
        }

        if (dictionary.IsUserDictionaryEntry(source, language))
        {
            return true;
        }

        if (dictionary is not IDirectFrequencyAutocorrectDictionary direct
            || direct.HasDirectFrequency(source, language))
        {
            return true;
        }

        // A morphology-only form has no direct corpus count. Preserve it by
        // default, but let the same narrow spelling probes used for unknown
        // input inspect it when the form has strong evidence of a typo.
        return !IsLikelyTerminalSoftSignOmission(source, language)
            && !TryGetLowFrequencyTerminalSoftSignCorrection(
                source,
                language,
                dictionary,
                out _)
            && !IsLowFrequencyRussianSpellingProbe(source, language, dictionary);
    }

    internal static bool HasAmbiguousNearestVowelCorrection(
        string source,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        if (language != TypingLanguage.Russian || source.Length < 4)
        {
            return false;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddVowelCorrections(source, candidates, maxDepth: 2);
        var valid = candidates
            .Where(candidate => dictionary.Contains(candidate, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (valid.Length < 2)
        {
            return false;
        }

        var minimumChanges = valid
            .Select(candidate => CountVowelChanges(source, candidate))
            .Where(static count => count > 0)
            .DefaultIfEmpty()
            .Min();
        var nearest = valid.Where(candidate => CountVowelChanges(source, candidate) == minimumChanges)
            .OrderByDescending(candidate => dictionary.GetFrequency(candidate, language))
            .ToArray();
        if (nearest.Length < 2)
        {
            return false;
        }

        var best = dictionary.GetFrequency(nearest[0], language);
        var second = dictionary.GetFrequency(nearest[1], language);
        return !(best >= 0.97 && best >= second + 0.05);
    }

    internal static bool HasForbiddenRussianDigraph(string source)
    {
        return source.Contains("жы", StringComparison.OrdinalIgnoreCase)
            || source.Contains("шы", StringComparison.OrdinalIgnoreCase)
            || source.Contains("чя", StringComparison.OrdinalIgnoreCase)
            || source.Contains("щя", StringComparison.OrdinalIgnoreCase)
            || source.Contains("чю", StringComparison.OrdinalIgnoreCase)
            || source.Contains("щю", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySelectUniqueStrongCandidate(
        IEnumerable<string> candidates,
        IAutocorrectDictionary dictionary,
        TypingLanguage language,
        out string correction)
    {
        var valid = candidates
            .Where(candidate => dictionary.Contains(candidate, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (valid.Length == 1
            && IsStrongEnough(valid[0], dictionary, language, minimum: 0.68))
        {
            correction = valid[0];
            return true;
        }

        correction = string.Empty;
        return false;
    }

    private static bool TrySelectUniqueDictionaryCandidate(
        IEnumerable<string> candidates,
        IAutocorrectDictionary dictionary,
        TypingLanguage language,
        out string correction)
    {
        var valid = candidates
            .Where(candidate => dictionary.Contains(candidate, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (valid.Length == 1)
        {
            correction = valid[0];
            return true;
        }

        correction = string.Empty;
        return false;
    }

    private static bool TrySelectStrongCandidate(
        IEnumerable<string> candidates,
        IAutocorrectDictionary dictionary,
        TypingLanguage language,
        out string correction)
    {
        var valid = candidates
            .Where(candidate => dictionary.Contains(candidate, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(candidate => dictionary.GetFrequency(candidate, language))
            .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (valid.Length == 1)
        {
            correction = valid[0];
            return true;
        }

        if (valid.Length > 1)
        {
            var bestFrequency = dictionary.GetFrequency(valid[0], language);
            var secondFrequency = dictionary.GetFrequency(valid[1], language);
            if (bestFrequency >= 0.85 && bestFrequency >= secondFrequency + 0.15)
            {
                correction = valid[0];
                return true;
            }
        }

        correction = string.Empty;
        return false;
    }

    private static bool TrySelectStructuralCandidate(
        string source,
        IEnumerable<string> candidates,
        IAutocorrectDictionary dictionary,
        TypingLanguage language,
        out string correction)
    {
        var valid = candidates
            .Where(candidate => dictionary.Contains(candidate, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (valid.Length == 0)
        {
            correction = string.Empty;
            return false;
        }

        var ranked = valid
            .Select(candidate => (Candidate: candidate, Signal: StructuralSignalCount(source, candidate)))
            .OrderByDescending(item => item.Signal)
            .ThenByDescending(item => dictionary.GetFrequency(item.Candidate, language))
            .ToArray();
        if (ranked.Length == 1 || ranked[0].Signal > ranked[1].Signal)
        {
            correction = ranked[0].Candidate;
            return true;
        }

        correction = string.Empty;
        return false;
    }

    private static bool TrySelectDoubleAndSoftSignCandidate(
        string source,
        IEnumerable<string> candidates,
        IAutocorrectDictionary dictionary,
        TypingLanguage language,
        out string correction)
    {
        var valid = candidates
            .Where(candidate => dictionary.Contains(candidate, language))
            .Where(candidate => ContainsNewDoubleConsonant(source, candidate))
            .Where(candidate => source.Contains("лн", StringComparison.OrdinalIgnoreCase)
                && candidate.Contains("льн", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (valid.Length == 1)
        {
            correction = valid[0];
            return true;
        }

        correction = string.Empty;
        return false;
    }

    private static bool TrySelectInsertionCandidate(
        string source,
        IEnumerable<string> candidates,
        IAutocorrectDictionary dictionary,
        TypingLanguage language,
        out string correction)
    {
        var valid = candidates
            .Where(candidate => candidate.Length == source.Length + 1)
            .Where(candidate => dictionary.Contains(candidate, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(candidate => dictionary.GetFrequency(candidate, language))
            .ToArray();
        if (valid.Length == 1)
        {
            correction = valid[0];
            return true;
        }

        if (valid.Length > 1)
        {
            var best = dictionary.GetFrequency(valid[0], language);
            var second = dictionary.GetFrequency(valid[1], language);
            if (best >= 0.85 && best >= second + 0.12)
            {
                correction = valid[0];
                return true;
            }
        }

        correction = string.Empty;
        return false;
    }

    private static bool TrySelectHighConfidenceInsertionCandidate(
        string source,
        IEnumerable<string> candidates,
        IAutocorrectDictionary dictionary,
        TypingLanguage language,
        out string correction)
    {
        var valid = candidates
            .Where(candidate => candidate.Length == source.Length + 1)
            .Where(candidate => dictionary.Contains(candidate, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(candidate => dictionary.GetFrequency(candidate, language))
            .ToArray();

        if (valid.Length == 0)
        {
            correction = string.Empty;
            return false;
        }

        var best = dictionary.GetFrequency(valid[0], language);
        var second = valid.Length > 1
            ? dictionary.GetFrequency(valid[1], language)
            : 0.0;
        if (best >= 0.90 && (valid.Length == 1 || best >= second + 0.10))
        {
            correction = valid[0];
            return true;
        }

        correction = string.Empty;
        return false;
    }

    private static bool TrySelectBestRepeatedCandidate(
        IEnumerable<string> candidates,
        IAutocorrectDictionary dictionary,
        TypingLanguage language,
        out string correction)
    {
        var valid = candidates
            .Where(candidate => dictionary.Contains(candidate, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(candidate => dictionary.GetFrequency(candidate, language))
            .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (valid.Length == 0)
        {
            correction = string.Empty;
            return false;
        }

        var best = dictionary.GetFrequency(valid[0], language);
        var second = valid.Length > 1
            ? dictionary.GetFrequency(valid[1], language)
            : 0.0;
        if (valid.Length == 1 || (best >= 0.85 && best >= second + 0.01))
        {
            correction = valid[0];
            return true;
        }

        correction = string.Empty;
        return false;
    }

    private static void AddStructuralCorrections(
        string source,
        ISet<string> candidates,
        int maxDepth)
    {
        var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source };
        for (var depth = 0; depth < maxDepth; depth++)
        {
            var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var current in frontier)
            {
                AddDigraphCorrections(current, next);
                AddDoubleConsonantCorrections(current, next);
                AddSoftSignCorrections(current, next);
                AddRepeatedCharacterCleanup(current, next);
            }

            foreach (var candidate in next)
            {
                if (!string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(candidate);
                }
            }

            frontier = next;
        }
    }

    private static void AddDoubleAndSoftSignCorrections(string source, ISet<string> candidates)
    {
        var doubled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDoubleConsonantCorrections(source, doubled);
        foreach (var candidate in doubled)
        {
            AddSoftSignCorrections(candidate, candidates);
        }
    }

    private static void AddDoubleConsonantCorrections(string source, ISet<string> candidates)
    {
        const string likelyDoubledConsonants = "лнстпмфкр";
        for (var index = 0; index < source.Length; index++)
        {
            if (!likelyDoubledConsonants.Contains(source[index], StringComparison.OrdinalIgnoreCase)
                || (index > 0 && source[index - 1] == source[index])
                || (index + 1 < source.Length && source[index + 1] == source[index]))
            {
                continue;
            }

            candidates.Add(source.Insert(index, source[index].ToString()));
        }
    }

    private static void AddSoftSignCorrections(string source, ISet<string> candidates)
    {
        if (source.Length >= 3)
        {
            candidates.Add(source + 'ь');
        }

        if (source.EndsWith('ь'))
        {
            candidates.Add(source[..^1]);
        }

        for (var index = 0; index < source.Length - 1; index++)
        {
            if (source[index] == 'л' && source[index + 1] == 'н')
            {
                candidates.Add(source[..(index + 1)] + 'ь' + source[(index + 1)..]);
            }
        }

    }

    private static void AddMissingIBeforeTerminalE(string source, ISet<string> candidates)
    {
        if (source.Length >= 5
            && source.EndsWith("не", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(source.Insert(source.Length - 1, "и"));
        }
    }

    private static void AddMissingEBeforeNSequence(string source, ISet<string> candidates)
    {
        for (var index = 0; index + 3 < source.Length; index++)
        {
            if (source[index + 1] == 'н'
                && source[index + 2] == 'и'
                && source[index + 3] == 'е')
            {
                candidates.Add(source.Insert(index + 1, "е"));
            }
        }
    }

    private static void AddMissingTerminalE(string source, ISet<string> candidates)
    {
        if (source.Length >= 5
            && source.EndsWith("ни", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(source + 'е');
        }
    }

    private static void AddMissingYBeforeTerminalS(string source, ISet<string> candidates)
    {
        if (source.Length >= 4
            && source.EndsWith("фес", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(source.Insert(source.Length - 1, "й"));
        }
    }

    private static bool TryGetDeterministicVowelCorrection(
        string source,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        out string correction)
    {
        correction = string.Empty;
        if (language != TypingLanguage.Russian || source.Length < 4)
        {
            return false;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddVowelCorrections(source, candidates, maxDepth: 2);
        var valid = candidates
            .Where(candidate => dictionary.Contains(candidate, language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (valid.Length == 0)
        {
            return false;
        }

        var minimumChanges = valid
            .Select(candidate => CountVowelChanges(source, candidate))
            .Where(static count => count > 0)
            .DefaultIfEmpty()
            .Min();
        var nearest = valid
            .Where(candidate => CountVowelChanges(source, candidate) == minimumChanges)
            .ToArray();
        if (minimumChanges == 1)
        {
            var rankedNearest = nearest
                .OrderByDescending(candidate => dictionary.GetFrequency(candidate, language))
                .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (rankedNearest.Length > 0
                && dictionary.GetFrequency(rankedNearest[0], language) >= 0.96
                && (rankedNearest.Length == 1
                    || dictionary.GetFrequency(rankedNearest[0], language)
                        >= dictionary.GetFrequency(rankedNearest[1], language) + 0.05))
            {
                correction = rankedNearest[0];
                return true;
            }

        }

        var hasHigherQualityAlternative = valid.Any(candidate =>
            CountVowelChanges(source, candidate) > minimumChanges
            && IsStrongEnough(candidate, dictionary, language, minimum: 0.95));
        if (minimumChanges >= 2
            && nearest.Length == 1
                && IsStrongEnough(nearest[0], dictionary, language, minimum: 0.85)
                && !hasHigherQualityAlternative)
        {
            correction = nearest[0];
            return true;
        }

        var ranked = valid
            .OrderByDescending(candidate => dictionary.GetFrequency(candidate, language))
            .ThenBy(candidate => CountVowelChanges(source, candidate))
            .ToArray();
        var strongestFrequency = dictionary.GetFrequency(ranked[0], language);
        var nextStrongestFrequency = ranked
            .Skip(1)
            .Select(candidate => dictionary.GetFrequency(candidate, language))
            .Where(static frequency => frequency > 0.75)
            .DefaultIfEmpty()
            .Max();
        if (CountVowelChanges(source, ranked[0]) >= 2
            && strongestFrequency >= 0.95
            && (nextStrongestFrequency == 0.0
                || strongestFrequency >= nextStrongestFrequency + 0.15))
        {
            correction = ranked[0];
            return true;
        }

        return false;
    }

    private static void AddSingleCharacterInsertions(string source, ISet<string> candidates)
    {
        const string russianAlphabet = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        for (var index = 0; index <= source.Length; index++)
        {
            foreach (var character in russianAlphabet)
            {
                candidates.Add(source.Insert(index, character.ToString()));
            }
        }
    }

    private static void AddAdjacentTranspositions(string source, ISet<string> candidates)
    {
        for (var index = 0; index + 1 < source.Length; index++)
        {
            if (source[index] == source[index + 1])
            {
                continue;
            }

            var buffer = source.ToCharArray();
            (buffer[index], buffer[index + 1]) = (buffer[index + 1], buffer[index]);
            candidates.Add(new string(buffer));
        }
    }

    private static void AddRepeatedCharacterCleanup(string source, ISet<string> candidates)
    {
        for (var index = 0; index + 1 < source.Length; index++)
        {
            if (source[index] == source[index + 1])
            {
                candidates.Add(source.Remove(index, 1));
            }
        }
    }

    private static int StructuralSignalCount(string source, string candidate)
    {
        var signal = 0;
        if (ContainsNewDoubleConsonant(source, candidate))
        {
            signal++;
        }

        if (source.EndsWith('ь') != candidate.EndsWith('ь'))
        {
            signal++;
        }

        if (source.Contains("лн", StringComparison.Ordinal)
            && candidate.Contains("льн", StringComparison.Ordinal))
        {
            signal++;
        }

        return signal;
    }

    private static bool ContainsNewDoubleConsonant(string source, string candidate)
    {
        for (var index = 0; index + 1 < candidate.Length; index++)
        {
            if (candidate[index] != candidate[index + 1]
                || !"бвгджзклмнпрстфхцчшщ".Contains(candidate[index], StringComparison.Ordinal))
            {
                continue;
            }

            var reduced = candidate.Remove(index, 1);
            if (source.Equals(reduced, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A double-consonant correction can be accompanied by the
            // independent лн→льн soft-sign correction. Permit that exact
            // two-signal shape, but do not use substring containment.
            for (var softSignIndex = 0; softSignIndex < reduced.Length; softSignIndex++)
            {
                if (reduced[softSignIndex] != 'ь')
                {
                    continue;
                }

                if (source.Equals(reduced.Remove(softSignIndex, 1), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountVowelChanges(string source, string candidate)
    {
        if (source.Length != candidate.Length)
        {
            return int.MaxValue;
        }

        var count = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == candidate[index])
            {
                continue;
            }

            if (!"аеёиоуыэюя".Contains(source[index], StringComparison.Ordinal)
                || !"аеёиоуыэюя".Contains(candidate[index], StringComparison.Ordinal))
            {
                return int.MaxValue;
            }

            count++;
        }

        return count;
    }

    private static void AddVowelCorrections(
        string source,
        ISet<string> candidates,
        int maxDepth)
    {
        var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source };
        for (var depth = 0; depth < maxDepth; depth++)
        {
            var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var current in frontier)
            {
                AddSingleVowelCorrections(current, next);
            }

            foreach (var candidate in next)
            {
                if (!string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(candidate);
                }
            }

            frontier = next;
        }
    }

    private static void AddSingleVowelCorrections(string source, ISet<string> candidates)
    {
        var pairs = new (char Left, char Right)[]
        {
            ('а', 'о'),
            ('о', 'а'),
            ('е', 'и'),
            ('и', 'е'),
            ('ы', 'и'),
            ('и', 'ы'),
            ('я', 'а'),
            ('а', 'я'),
            ('ё', 'е'),
            ('е', 'ё'),
        };

        for (var index = 0; index < source.Length; index++)
        {
            foreach (var (left, right) in pairs)
            {
                if (source[index] != left)
                {
                    continue;
                }

                var candidate = source.ToCharArray();
                candidate[index] = right;
                candidates.Add(new string(candidate));
            }
        }
    }

    private static bool IsStrongEnough(
        string candidate,
        IAutocorrectDictionary dictionary,
        TypingLanguage language,
        double minimum)
    {
        return dictionary.GetFrequency(candidate, language) >= minimum;
    }

    private static void AddDigraphCorrections(string source, ISet<string> candidates)
    {
        var replacements = new (string From, string To)[]
        {
            ("жы", "жи"),
            ("шы", "ши"),
            ("чя", "ча"),
            ("щя", "ща"),
            ("чю", "чу"),
            ("щю", "щу"),
            ("здел", "сдел"),
        };

        foreach (var (from, to) in replacements)
        {
            for (var index = source.IndexOf(from, StringComparison.OrdinalIgnoreCase);
                 index >= 0;
                 index = source.IndexOf(from, index + 1, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(source[..index] + to + source[(index + from.Length)..]);
            }
        }
    }

}
