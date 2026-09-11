using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Engines;

/// <summary>
/// Operation-aware spelling apply gate driven by reverse mutation analysis.
/// </summary>
internal static class OperationPrecisionGate
{
    private const double NearEqualEditGap = 0.08;
    private const double CredibleExtendedBand = 0.35;
    private const double CrossOperationBand = 0.70;
    private const double ClearFrequencyMargin = 0.12;

    internal static bool AllowsSpellingApply(
        string token,
        string replacement,
        TypingLanguage? sourceLanguage,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        SentenceLanguageHint? languageHint = null)
    {
        if (sourceLanguage is null)
        {
            return false;
        }

        if (languageHint?.HasStrongEnglish == true
            && sourceLanguage == TypingLanguage.English
            && dictionary.IsNeverAutocorrect(token, TypingLanguage.English))
        {
            return false;
        }

        var analysis = MutationClassificationOracle.Analyze(token, sourceLanguage.Value, dictionary, options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable
            || string.IsNullOrEmpty(analysis.UniqueTarget))
        {
            return false;
        }

        if (!string.Equals(analysis.UniqueTarget, replacement, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Unique only after discarding another credible parent → Wait.
        if (HasDiscardedCredibleCompetitor(analysis))
        {
            return false;
        }

        return analysis.Operation switch
        {
            EditOperationType.RepeatedAccidentalCharacter => AllowsRepeatedCharacter(
                token,
                replacement,
                sourceLanguage.Value,
                dictionary),
            EditOperationType.AdjacentTransposition => true,
            EditOperationType.MissingCharacter or EditOperationType.ExtraCharacter =>
                dictionary.GetFrequency(replacement, sourceLanguage.Value) >= 0.70,
            EditOperationType.VowelSubstitution or EditOperationType.GeneralSubstitution
                or EditOperationType.AdjacentKeySubstitution => AllowsSubstitution(
                    token,
                    replacement,
                    sourceLanguage.Value,
                    dictionary,
                    analysis),
            _ => false,
        };
    }

    /// <summary>
    /// True when Unique survived only by discarding another credible near-cost parent.
    /// Production evidence only — does not consult intended corpus sources.
    /// </summary>
    internal static bool HasDiscardedCredibleCompetitor(MutationAnalysisResult analysis)
    {
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable
            || string.IsNullOrEmpty(analysis.UniqueTarget)
            || analysis.Sources.Count == 0)
        {
            return false;
        }

        if (!TryGetWinner(analysis, out var winner))
        {
            return true;
        }

        // Perfect-tier adjacent-transposition ultra (teh/adn ≈ 1.0) may Apply even with
        // longer MissingCharacter collisions (tech/hadn). Near-ultra winners (два/let)
        // still Wait on credible longer MC parents.
        if (winner.Operation == EditOperationType.AdjacentTransposition
            && winner.Frequency >= 0.9995)
        {
            return false;
        }

        // A short terminal doubled-letter typo such as `мирр` has a single
        // unambiguous repeated-character reduction (`мир`).  Adjacent-key
        // neighbours in the broad lexicon must not turn this very common
        // cleanup into Wait; keep the exception bounded to short, strongly
        // attested targets so it cannot weaken the general ambiguity rules.
        if (winner.Operation == EditOperationType.RepeatedAccidentalCharacter
            && TokenScriptAnalyzer.Classify(winner.Word) == TokenScript.Cyrillic
            && winner.Word.Length <= 3
            && winner.Frequency >= 0.85
            && analysis.Sources.Count(source =>
                source.Operation == EditOperationType.RepeatedAccidentalCharacter) == 1
            && !analysis.Sources.Any(source =>
                !string.Equals(source.Word, winner.Word, StringComparison.OrdinalIgnoreCase)
                && IsCredibleLongerStructuralCompetitor(winner, source)))
        {
            return false;
        }

        foreach (var alternate in analysis.Sources)
        {
            if (string.Equals(alternate.Word, analysis.UniqueTarget, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsCredibleLongerStructuralCompetitor(winner, alternate)
                || IsCredibleNonPrefixLongerShorteningCompetitor(winner, alternate)
                || IsCredibleInternalInsertionGenSubCompetitor(winner, alternate)
                || IsCredibleSuffixGeneralSubstitutionExtension(winner, alternate)
                || IsCredibleSameLengthMissingCharacterPeer(winner, alternate)
                || IsCredibleTranspositionSameLengthPeer(winner, alternate)
                || IsCredibleSameLengthNearCostPeer(winner, alternate)
                || IsCredibleVowelSameLengthGenSubPeer(winner, alternate)
                || IsCredibleVowelSameLengthVowelPeer(winner, alternate)
                || IsCredibleShorterTokenLengthPeer(winner, alternate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Decisive Unique lead that may release non-C1/C2 Wait without treating the
    /// competitor as a credible discarded parent.
    /// </summary>
    internal static bool HasDecisiveUniqueFrequencyLead(
        PossibleMutationSource winner,
        PossibleMutationSource alternate)
        => winner.Frequency >= 0.90
            && winner.Frequency >= alternate.Frequency + ClearFrequencyMargin
            && winner.EditCost <= alternate.EditCost + NearEqualEditGap;

    /// <summary>
    /// Fine-grained C1/C2/C3 blocking label for recovery-loss reports (test/report only).
    /// </summary>
    internal static string ClassifyRecoveryBlockGroup(
        PossibleMutationSource winner,
        PossibleMutationSource alternate)
    {
        if (IsCredibleLongerStructuralCompetitor(winner, alternate))
        {
            return alternate.Operation switch
            {
                EditOperationType.MissingCharacter => "prev_missing_character_parent",
                EditOperationType.AdjacentTransposition => "prev_longer_transposition_parent",
                EditOperationType.VowelSubstitution => "prev_longer_vowel_parent",
                _ => "prev_longer_structural_parent",
            };
        }

        if (IsCredibleNonPrefixLongerShorteningCompetitor(winner, alternate))
        {
            return "C2_nonprefix_longer_gensub";
        }

        if (IsCredibleInternalInsertionGenSubCompetitor(winner, alternate))
        {
            return "C2_internal_insert";
        }

        if (IsCredibleSuffixGeneralSubstitutionExtension(winner, alternate))
        {
            return "prev_suffix_gensub";
        }

        if (IsCredibleSameLengthMissingCharacterPeer(winner, alternate))
        {
            return "prev_same_length_missing_peer";
        }

        if (IsCredibleTranspositionSameLengthPeer(winner, alternate))
        {
            if (alternate.Operation == EditOperationType.VowelSubstitution)
            {
                return "C1_vowel_peer";
            }

            if (alternate.Frequency >= winner.Frequency)
            {
                return "C1_peer_leads";
            }

            if (AreLetterAnagrams(winner.Word, alternate.Word))
            {
                return "C1_anagram";
            }

            return "C1_strong_unique_gensub";
        }

        if (IsCredibleSameLengthNearCostPeer(winner, alternate))
        {
            return "other_same_length_near_cost";
        }

        if (IsCredibleVowelSameLengthGenSubPeer(winner, alternate))
        {
            return "other_vowel_same_length_gensub";
        }

        if (IsCredibleVowelSameLengthVowelPeer(winner, alternate))
        {
            return "C1_vowel_peer";
        }

        if (IsCredibleShorterTokenLengthPeer(winner, alternate))
        {
            return "C3_shorter_peer";
        }

        return "other";
    }

    /// <summary>
    /// True when alternate remains inside the canonical cross-operation one-edit cost band.
    /// </summary>
    internal static bool IsGenuineOneEditExplanation(
        PossibleMutationSource winner,
        PossibleMutationSource alternate)
        => IsCredibleNearCostParent(winner, alternate);

    /// <summary>
    /// Why HasDiscardedCredibleCompetitor returned true for this alternate (test/report only).
    /// </summary>
    internal static string DiagnoseDiscardReason(
        PossibleMutationSource winner,
        PossibleMutationSource alternate)
    {
        if (IsCredibleLongerStructuralCompetitor(winner, alternate))
        {
            return alternate.Operation switch
            {
                EditOperationType.MissingCharacter => "R1_missing_character_parent",
                EditOperationType.AdjacentTransposition => "R1_longer_transposition_parent",
                EditOperationType.VowelSubstitution => "R1_longer_vowel_parent",
                _ => "R1_longer_structural_parent",
            };
        }

        if (IsCredibleNonPrefixLongerShorteningCompetitor(winner, alternate))
        {
            return "R1_nonprefix_longer_shortening";
        }

        if (IsCredibleInternalInsertionGenSubCompetitor(winner, alternate))
        {
            return "R1_internal_insertion_gensub";
        }

        if (IsCredibleSuffixGeneralSubstitutionExtension(winner, alternate))
        {
            return "R1_suffix_gensub_extension";
        }

        if (IsCredibleSameLengthMissingCharacterPeer(winner, alternate))
        {
            return "R1_same_length_missing_peer";
        }

        if (IsCredibleTranspositionSameLengthPeer(winner, alternate))
        {
            return "R1_transp_same_length_peer";
        }

        if (IsCredibleSameLengthNearCostPeer(winner, alternate))
        {
            return "R1_same_length_near_cost_peer";
        }

        if (IsCredibleVowelSameLengthGenSubPeer(winner, alternate))
        {
            return "R1_vowel_same_length_gensub";
        }

        if (IsCredibleVowelSameLengthVowelPeer(winner, alternate))
        {
            return "R1_vowel_same_length_vowel_peer";
        }

        if (IsCredibleShorterTokenLengthPeer(winner, alternate))
        {
            return "R1_shorter_token_length_peer";
        }

        if (IsClearlyStrongerWinner(winner, alternate))
        {
            return "clearly_stronger_skip";
        }

        if (IsCredibleNearCostParent(winner, alternate) || alternate.Frequency >= 0.70)
        {
            return "catch_all_credible_peer";
        }

        return "none";
    }

    /// <summary>
    /// First alternate that forces discarded-competitor Wait (test/report only).
    /// Mirrors <see cref="HasDiscardedCredibleCompetitor"/> loop order.
    /// </summary>
    internal static bool TryGetForcingAlternate(
        MutationAnalysisResult analysis,
        out PossibleMutationSource winner,
        out PossibleMutationSource alternate,
        out string reason)
    {
        winner = default;
        alternate = default;
        reason = "none";
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable
            || string.IsNullOrEmpty(analysis.UniqueTarget)
            || !TryGetWinner(analysis, out winner))
        {
            return false;
        }

        if (winner.Operation == EditOperationType.AdjacentTransposition
            && winner.Frequency >= 0.9995)
        {
            return false;
        }

        foreach (var candidate in analysis.Sources)
        {
            if (string.Equals(candidate.Word, analysis.UniqueTarget, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsCredibleLongerStructuralCompetitor(winner, candidate))
            {
                alternate = candidate;
                reason = candidate.Operation switch
                {
                    EditOperationType.MissingCharacter => "R1_missing_character_parent",
                    EditOperationType.AdjacentTransposition => "R1_longer_transposition_parent",
                    EditOperationType.VowelSubstitution => "R1_longer_vowel_parent",
                    _ => "R1_longer_structural_parent",
                };
                return true;
            }

            if (IsCredibleNonPrefixLongerShorteningCompetitor(winner, candidate))
            {
                alternate = candidate;
                reason = "R1_nonprefix_longer_shortening";
                return true;
            }

            if (IsCredibleInternalInsertionGenSubCompetitor(winner, candidate))
            {
                alternate = candidate;
                reason = "R1_internal_insertion_gensub";
                return true;
            }

            if (IsCredibleSuffixGeneralSubstitutionExtension(winner, candidate))
            {
                alternate = candidate;
                reason = "R1_suffix_gensub_extension";
                return true;
            }

            if (IsCredibleSameLengthMissingCharacterPeer(winner, candidate))
            {
                alternate = candidate;
                reason = "R1_same_length_missing_peer";
                return true;
            }

            if (IsCredibleTranspositionSameLengthPeer(winner, candidate))
            {
                alternate = candidate;
                reason = "R1_transp_same_length_peer";
                return true;
            }

            if (IsCredibleSameLengthNearCostPeer(winner, candidate))
            {
                alternate = candidate;
                reason = "R1_same_length_near_cost_peer";
                return true;
            }

            if (IsCredibleVowelSameLengthGenSubPeer(winner, candidate))
            {
                alternate = candidate;
                reason = "R1_vowel_same_length_gensub";
                return true;
            }

            if (IsCredibleVowelSameLengthVowelPeer(winner, candidate))
            {
                alternate = candidate;
                reason = "R1_vowel_same_length_vowel_peer";
                return true;
            }

            if (IsCredibleShorterTokenLengthPeer(winner, candidate))
            {
                alternate = candidate;
                reason = "R1_shorter_token_length_peer";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Longer one-edit competitor of the typed mutation via structural ops
    /// (MissingCharacter / AdjacentTransposition / VowelSubstitution).
    /// Does not include GeneralSubstitution or AdjacentKey longer collisions.
    /// </summary>
    internal static bool IsCredibleLongerStructuralCompetitor(
        PossibleMutationSource winner,
        PossibleMutationSource parent)
    {
        if (parent.Word.Length <= winner.Word.Length
            || parent.Frequency < 0.70
            || parent.EditCost > winner.EditCost + CrossOperationBand)
        {
            return false;
        }

        // A longer MissingCharacter parent is a genuine competing reading even
        // when the shorter AdjacentKey winner is somewhat more frequent. Do not
        // let a modest frequency lead turn `анютк` into an automatic `анюта`
        // when `анютка` is an equally plausible one-edit completion.
        if (parent.Operation == EditOperationType.MissingCharacter
            && winner.Operation == EditOperationType.AdjacentKeySubstitution)
        {
            return true;
        }

        if (HasDecisiveUniqueFrequencyLead(winner, parent))
        {
            return false;
        }

        return parent.Operation is EditOperationType.MissingCharacter
            or EditOperationType.AdjacentTransposition
            or EditOperationType.VowelSubstitution;
    }

    /// <summary>
    /// Longer GenSub/AdjKey competitor against Extra Unique that is not a pure prefix
    /// collision (аалка→алка vs галка/палка). RepeatedAccidental shortenings use
    /// <see cref="IsCredibleInternalInsertionGenSubCompetitor"/> instead.
    /// </summary>
    internal static bool IsCredibleNonPrefixLongerShorteningCompetitor(
        PossibleMutationSource winner,
        PossibleMutationSource parent)
    {
        if (winner.Operation == EditOperationType.ExtraCharacter)
        {
            if (parent.Operation is not (EditOperationType.GeneralSubstitution
                or EditOperationType.AdjacentKeySubstitution))
            {
                return false;
            }
        }
        else if (winner.Operation == EditOperationType.RepeatedAccidentalCharacter)
        {
            // Repeated + AdjKey longer (bellt→belt vs belly); GenSub uses internal-insertion rule.
            if (parent.Operation != EditOperationType.AdjacentKeySubstitution)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        if (parent.Word.Length <= winner.Word.Length
            || parent.Frequency < 0.70
            || parent.EditCost > winner.EditCost + CrossOperationBand)
        {
            return false;
        }

        // Prefix collision: longer word is Unique with a leading character (галка/алка).
        if (parent.Word.EndsWith(winner.Word, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Internal insertion GenSub parent of an Extra/Repeated Unique (amy/army, best/beast).
    /// Excludes prefix (галка) and suffix (actors) affix collisions.
    /// </summary>
    internal static bool IsCredibleInternalInsertionGenSubCompetitor(
        PossibleMutationSource winner,
        PossibleMutationSource parent)
    {
        if (winner.Operation is not (EditOperationType.ExtraCharacter
                or EditOperationType.RepeatedAccidentalCharacter)
            || parent.Operation != EditOperationType.GeneralSubstitution
            || parent.Word.Length <= winner.Word.Length
            || parent.Frequency < 0.70
            || parent.EditCost > winner.EditCost + CrossOperationBand)
        {
            return false;
        }

        if (parent.Word.StartsWith(winner.Word, StringComparison.OrdinalIgnoreCase)
            || parent.Word.EndsWith(winner.Word, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Insertion-only: early inserts (cause/clause, amy/army) always Wait; late inserts
        // need a near-tied peer (алена/аленка with weak peer → Apply).
        if (IsInsertionOnlyExtension(winner.Word, parent.Word))
        {
            var insertIndex = FindSingleInsertIndex(winner.Word, parent.Word);
            if (insertIndex >= 0 && insertIndex <= 2)
            {
                return true;
            }

            return parent.Frequency + 0.02 >= winner.Frequency
                || winner.Frequency < 0.90;
        }

        // Non-subsequence len+1 GenSub (cidder/bidder, joob/job): short shared prefix means
        // a genuine alternate stem. Longer shared prefixes with weak peers may Apply.
        if (parent.Word.Length == winner.Word.Length + 1)
        {
            if (CommonPrefixLength(winner.Word, parent.Word) <= 1)
            {
                return true;
            }

            return parent.Frequency + 0.02 >= winner.Frequency
                || winner.Frequency < 0.90;
        }

        return false;
    }

    /// <summary>Index of the single inserted character in <paramref name="longer"/>, or -1.</summary>
    private static int FindSingleInsertIndex(string shorter, string longer)
    {
        if (longer.Length != shorter.Length + 1)
        {
            return -1;
        }

        for (var i = 0; i < shorter.Length; i++)
        {
            if (char.ToLowerInvariant(shorter[i]) == char.ToLowerInvariant(longer[i]))
            {
                continue;
            }

            for (var k = i; k < shorter.Length; k++)
            {
                if (char.ToLowerInvariant(shorter[k]) != char.ToLowerInvariant(longer[k + 1]))
                {
                    return -1;
                }
            }

            return i;
        }

        return shorter.Length;
    }

    /// <summary>
    /// AdjacentTransposition Unique vs same-length GenSub/Vowel peer (acke→cake vs ache).
    /// Perfect-tier ultra winners (teh/adn) are handled by the HasDiscarded early bypass.
    /// Modest GenSub dominance band [0.07, 0.10) preserves recieve→receive without
    /// releasing stronger wrong-Unique cases (cake/ache) or weaker ones (laws/alas).
    /// </summary>
    internal static bool IsCredibleTranspositionSameLengthPeer(
        PossibleMutationSource winner,
        PossibleMutationSource peer)
    {
        if (winner.Operation != EditOperationType.AdjacentTransposition
            || winner.Frequency >= 0.9995
            || peer.Word.Length != winner.Word.Length
            || peer.Frequency < 0.70
            || peer.EditCost > winner.EditCost + CrossOperationBand)
        {
            return false;
        }

        if (peer.Operation == EditOperationType.VowelSubstitution)
        {
            return true;
        }

        if (peer.Operation != EditOperationType.GeneralSubstitution)
        {
            return false;
        }

        // recieve→receive vs relieve: modest frequency edge over a farther GenSub.
        if (winner.Frequency >= 0.96
            && peer.EditCost >= winner.EditCost + NearEqualEditGap)
        {
            var margin = winner.Frequency - peer.Frequency;
            if (margin >= 0.07 && margin < 0.10)
            {
                return false;
            }
        }

        // Residual C1 shape: peer leads, anagram competitor (cake/ache), or strong Unique
        // with a still-credible GenSub peer (chance/chanel). Mid Unique that already leads
        // a non-anagram GenSub (аизя→азия vs гизя) remains Apply for recovery.
        if (peer.Frequency >= winner.Frequency)
        {
            return true;
        }

        if (AreLetterAnagrams(winner.Word, peer.Word))
        {
            return true;
        }

        if (winner.Frequency >= 0.92 && peer.Frequency >= 0.75)
        {
            return true;
        }

        return false;
    }

    private static bool AreLetterAnagrams(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var leftChars = left.Select(char.ToLowerInvariant).OrderBy(static c => c).ToArray();
        var rightChars = right.Select(char.ToLowerInvariant).OrderBy(static c => c).ToArray();
        return leftChars.SequenceEqual(rightChars);
    }

    /// <summary>
    /// MissingCharacter Unique that lengthens the token vs a same-length-as-token peer
    /// (боться→бояться vs биться; hmor→humor vs amor). Genuine one-edit of the typed token.
    /// </summary>
    internal static bool IsCredibleShorterTokenLengthPeer(
        PossibleMutationSource winner,
        PossibleMutationSource peer)
    {
        if (winner.Operation != EditOperationType.MissingCharacter
            || IsDocumentedUltraDominanceException(winner)
            || peer.Word.Length != winner.Word.Length - 1
            || peer.Frequency < 0.70
            || peer.EditCost > winner.EditCost + CrossOperationBand)
        {
            return false;
        }

        if (peer.Operation is not (EditOperationType.GeneralSubstitution
            or EditOperationType.VowelSubstitution
            or EditOperationType.AdjacentKeySubstitution
            or EditOperationType.AdjacentTransposition))
        {
            return false;
        }

        // Clear Unique lead over a weaker GenSub shorter peer may Apply
        // (авгут→август vs аргут). hmor→humor vs amor stays Wait (Unique < 0.95).
        // Vowel/transp shorter peers still Wait (боться→бояться vs биться).
        if (peer.Operation == EditOperationType.GeneralSubstitution
            && winner.Frequency >= peer.Frequency + ClearFrequencyMargin
            && winner.Frequency >= 0.95)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Longer GeneralSubstitution parent that is a suffix extension of Unique
    /// (actorr→actor vs actors). Excludes prefix collisions (аалка→алка vs галка).
    /// </summary>
    internal static bool IsCredibleSuffixGeneralSubstitutionExtension(
        PossibleMutationSource winner,
        PossibleMutationSource parent)
    {
        if (HasDecisiveUniqueFrequencyLead(winner, parent))
        {
            return false;
        }

        if (parent.Operation != EditOperationType.GeneralSubstitution
            || winner.Operation is not (EditOperationType.ExtraCharacter
                or EditOperationType.RepeatedAccidentalCharacter)
            || parent.Word.Length <= winner.Word.Length
            || parent.Frequency < 0.70
            || parent.EditCost > winner.EditCost + CrossOperationBand)
        {
            return false;
        }

        return parent.Word.StartsWith(winner.Word, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when <paramref name="parent"/> is a credible MissingCharacter recovery of the
    /// typed mutation competing with Unique — including cases where Unique is a different
    /// same-length substitution (вилкин→силкин vs авилкин), not only insertion-extensions
    /// of Unique (йда→да vs айда).
    /// </summary>
    internal static bool IsCredibleMissingCharacterCompetitor(
        PossibleMutationSource winner,
        PossibleMutationSource parent)
    {
        return IsCredibleLongerStructuralCompetitor(winner, parent)
            && parent.Operation == EditOperationType.MissingCharacter;
    }

    /// <summary>
    /// True when Unique is a shortening of a longer credible near-cost parent
    /// formed by insertions only, and that parent is a MissingCharacter recovery
    /// of the same typed mutation (йда→да vs айда; not алка vs палка collision).
    /// Kept as a named alias for focused tests.
    /// </summary>
    internal static bool IsCredibleLongerParentShortening(
        PossibleMutationSource winner,
        PossibleMutationSource parent)
    {
        if (!IsCredibleMissingCharacterCompetitor(winner, parent))
        {
            return false;
        }

        return IsInsertionOnlyExtension(winner.Word, parent.Word);
    }

    /// <summary>
    /// Same-length MissingCharacter peers (ooks→looks vs books) remain ambiguous.
    /// </summary>
    internal static bool IsCredibleSameLengthMissingCharacterPeer(
        PossibleMutationSource winner,
        PossibleMutationSource parent)
    {
        if (HasDecisiveUniqueFrequencyLead(winner, parent))
        {
            return false;
        }

        if (parent.Word.Length != winner.Word.Length
            || parent.Frequency < 0.70
            || parent.EditCost > winner.EditCost + CrossOperationBand)
        {
            return false;
        }

        return winner.Operation == EditOperationType.MissingCharacter
            && parent.Operation == EditOperationType.MissingCharacter;
    }

    /// <summary>
    /// Same-length near-cost peer that remains a competing one-edit explanation after
    /// Unique frequency dominance (ыверь→дверь vs аверь). Limited to AdjKey/GenSub
    /// Unique winners — Vowel winners use <see cref="IsCredibleVowelSameLengthGenSubPeer"/>.
    /// </summary>
    internal static bool IsCredibleSameLengthNearCostPeer(
        PossibleMutationSource winner,
        PossibleMutationSource peer)
    {
        if (HasDecisiveUniqueFrequencyLead(winner, peer))
        {
            return false;
        }

        if (winner.Operation is not (EditOperationType.AdjacentKeySubstitution
            or EditOperationType.GeneralSubstitution))
        {
            return false;
        }

        if (peer.Word.Length != winner.Word.Length
            || peer.Frequency < 0.70
            || peer.EditCost > winner.EditCost + CrossOperationBand)
        {
            return false;
        }

        if (IsDocumentedUltraDominanceException(winner))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Vowel Unique vs same-length GenSub peer.
    /// Hamming-1: same-slot competition (caru→care vs cart; casi→case vs cass).
    /// Hamming-2 with a long shared prefix and non-ultra vowel: stem-split peers
    /// (activety→activity vs actively). Multi-slot distant peers (миняй→меняй vs митяй)
    /// remain Apply.
    /// </summary>
    internal static bool IsCredibleVowelSameLengthGenSubPeer(
        PossibleMutationSource winner,
        PossibleMutationSource peer)
    {
        if (winner.Operation != EditOperationType.VowelSubstitution
            || peer.Operation != EditOperationType.GeneralSubstitution
            || peer.Word.Length != winner.Word.Length
            || peer.Frequency < 0.70
            || peer.EditCost > winner.EditCost + CrossOperationBand)
        {
            return false;
        }

        var hamming = HammingDistance(winner.Word, peer.Word);
        if (hamming == 1)
        {
            // Mid-high vowel Unique (0.95–0.99) over a same-slot GenSub may Apply for
            // recovery (уленка→аленка vs пленка). Near-ultra same-slot (caru/casi) and
            // weaker vowels (chimes/activety-scale) still Wait.
            if (winner.Frequency is >= 0.95 and < 0.99)
            {
                return false;
            }

            if (winner.Frequency >= 0.99
                && winner.Frequency >= peer.Frequency + ClearFrequencyMargin)
            {
                return false;
            }

            return true;
        }

        // activity/actively: two-slot stem split; exclude миняй (short shared prefix).
        if (hamming == 2
            && winner.Frequency < 0.98
            && CommonPrefixLength(winner.Word, peer.Word) >= winner.Word.Length - 3)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Vowel Unique vs same-length Vowel peer (былочка→булочка vs белочка).
    /// Unlike GenSub mid-high recovery, vowel-vowel same-slot peers remain Wait —
    /// they are the OracleProductionDisagreement AmbiguousApplied cluster.
    /// </summary>
    internal static bool IsCredibleVowelSameLengthVowelPeer(
        PossibleMutationSource winner,
        PossibleMutationSource peer)
    {
        if (winner.Operation != EditOperationType.VowelSubstitution
            || peer.Operation != EditOperationType.VowelSubstitution
            || peer.Word.Length != winner.Word.Length
            || peer.Frequency < 0.70
            || peer.EditCost > winner.EditCost + CrossOperationBand)
        {
            return false;
        }

        return HammingDistance(winner.Word, peer.Word) >= 1;
    }

    private static int HammingDistance(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return int.MaxValue;
        }

        var distance = 0;
        for (var i = 0; i < left.Length; i++)
        {
            if (char.ToLowerInvariant(left[i]) != char.ToLowerInvariant(right[i]))
            {
                distance++;
            }
        }

        return distance;
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var limit = Math.Min(left.Length, right.Length);
        var length = 0;
        for (; length < limit; length++)
        {
            if (char.ToLowerInvariant(left[length]) != char.ToLowerInvariant(right[length]))
            {
                break;
            }
        }

        return length;
    }

    private static bool IsInsertionOnlyExtension(string shorter, string longer)
    {
        if (longer.Length <= shorter.Length)
        {
            return false;
        }

        var i = 0;
        var j = 0;
        while (i < shorter.Length && j < longer.Length)
        {
            if (char.ToLowerInvariant(shorter[i]) == char.ToLowerInvariant(longer[j]))
            {
                i++;
                j++;
            }
            else
            {
                j++;
            }
        }

        return i == shorter.Length;
    }

    /// <summary>
    /// Whether <paramref name="parent"/> remains a credible near-cost explanation
    /// competing with the Unique winner (shared by production Apply and tests).
    /// </summary>
    internal static bool IsCredibleNearCostParent(
        PossibleMutationSource winner,
        PossibleMutationSource parent)
    {
        if (parent.Frequency < 0.50)
        {
            return false;
        }

        var band = winner.Operation == parent.Operation
            ? CredibleExtendedBand
            : CrossOperationBand;

        return parent.EditCost <= winner.EditCost + band;
    }

    /// <summary>
    /// Generated-case helper: Unique≠intended is Ambiguous when intended remains a
    /// credible near-cost parent that was not clearly dominated.
    /// </summary>
    internal static bool ShouldRemapUniqueAsAmbiguous(
        MutationAnalysisResult analysis,
        string intendedSource)
    {
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable
            || string.IsNullOrEmpty(analysis.UniqueTarget)
            || string.Equals(analysis.UniqueTarget, intendedSource, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryGetWinner(analysis, out var winner))
        {
            return true;
        }

        // Same ultra-dominance exceptions as production Apply (helo/teh/adn).
        if (winner.Operation == EditOperationType.AdjacentTransposition
            && winner.Frequency >= 0.9995)
        {
            return false;
        }

        var intended = analysis.Sources
            .Where(source => string.Equals(source.Word, intendedSource, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static source => source.EditCost)
            .ThenByDescending(static source => source.Frequency)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(intended.Word))
        {
            return false;
        }

        if (IsCredibleLongerStructuralCompetitor(winner, intended)
            || IsCredibleNonPrefixLongerShorteningCompetitor(winner, intended)
            || IsCredibleInternalInsertionGenSubCompetitor(winner, intended)
            || IsCredibleSuffixGeneralSubstitutionExtension(winner, intended)
            || IsCredibleSameLengthMissingCharacterPeer(winner, intended)
            || IsCredibleTranspositionSameLengthPeer(winner, intended)
            || IsCredibleSameLengthNearCostPeer(winner, intended)
            || IsCredibleVowelSameLengthGenSubPeer(winner, intended)
            || IsCredibleVowelSameLengthVowelPeer(winner, intended)
            || IsCredibleShorterTokenLengthPeer(winner, intended))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetWinner(
        MutationAnalysisResult analysis,
        out PossibleMutationSource winner)
    {
        winner = analysis.Sources
            .Where(source => string.Equals(
                source.Word,
                analysis.UniqueTarget,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(static source => source.EditCost)
            .ThenByDescending(static source => source.Frequency)
            .FirstOrDefault();
        return !string.IsNullOrEmpty(winner.Word);
    }

    /// <summary>Test/report helper.</summary>
    internal static bool TryGetWinnerPublic(
        MutationAnalysisResult analysis,
        out PossibleMutationSource winner)
        => TryGetWinner(analysis, out winner);

    /// <summary>Test/report helper.</summary>
    internal static bool IsDocumentedUltraDominanceExceptionPublic(PossibleMutationSource winner)
        => IsDocumentedUltraDominanceException(winner);

    private static bool IsDocumentedUltraDominanceException(PossibleMutationSource winner)
    {
        // helo→hello, similar ultra-freq insert/delete.
        if (winner.Operation is EditOperationType.MissingCharacter or EditOperationType.ExtraCharacter
            && winner.Frequency >= 0.999)
        {
            return true;
        }

        // teh→the, adn→and.
        if (winner.Operation == EditOperationType.AdjacentTransposition
            && winner.Frequency >= 0.998)
        {
            return true;
        }

        return false;
    }

    private static bool IsClearlyStrongerWinner(
        PossibleMutationSource winner,
        PossibleMutationSource alternate)
    {
        if (IsDocumentedUltraDominanceException(winner))
        {
            return true;
        }

        if (winner.Frequency >= 0.995
            && winner.Frequency >= alternate.Frequency + 0.004)
        {
            return true;
        }

        if (winner.Frequency >= alternate.Frequency + ClearFrequencyMargin)
        {
            return true;
        }

        // teh→the / adn→and / recieve→receive: transposition Unique over a farther general sub.
        if (winner.Operation == EditOperationType.AdjacentTransposition
            && winner.Frequency >= 0.96
            && alternate.Operation == EditOperationType.GeneralSubstitution
            && alternate.EditCost >= winner.EditCost + NearEqualEditGap
            && winner.Frequency >= alternate.Frequency + 0.05)
        {
            return true;
        }

        // миняй→меняй: vowel Unique clearly preferred over a farther general substitute.
        if (winner.Operation == EditOperationType.VowelSubstitution
            && alternate.Operation == EditOperationType.GeneralSubstitution
            && winner.Frequency >= 0.98
            && alternate.EditCost >= winner.EditCost + NearEqualEditGap + 0.01)
        {
            return true;
        }

        // превет→привет style: ultra-freq vowel with no near-cost peer.
        if (winner.Operation == EditOperationType.VowelSubstitution
            && winner.Frequency >= 0.999
            && alternate.EditCost > winner.EditCost + NearEqualEditGap)
        {
            return true;
        }

        return false;
    }

    private static bool AllowsRepeatedCharacter(
        string token,
        string replacement,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        if (TrustedWordAnalyzer.IsExactKnownOriginal(token, dictionary))
        {
            return false;
        }

        if (!AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(token, replacement))
        {
            return false;
        }

        if (!dictionary.Contains(replacement, language)
            || dictionary.GetFrequency(replacement, language) < 0.65)
        {
            return false;
        }

        var runTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < token.Length - 1; index++)
        {
            if (token[index] != token[index + 1])
            {
                continue;
            }

            var reduced = token.Remove(index, 1);
            if (dictionary.Contains(reduced, language)
                && dictionary.GetFrequency(reduced, language) >= 0.65)
            {
                runTargets.Add(reduced);
            }
        }

        return runTargets.Count == 1
            && runTargets.Contains(replacement);
    }

    private static bool AllowsSubstitution(
        string token,
        string replacement,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        MutationAnalysisResult analysis)
    {
        if (TrustedWordAnalyzer.IsExactKnownOriginal(token, dictionary))
        {
            return false;
        }

        var frequency = dictionary.GetFrequency(replacement, language);
        if (frequency < 0.65)
        {
            return false;
        }

        var minimumCost = analysis.Sources.Min(static source => source.EditCost);
        var sameCostCompetitors = analysis.Sources
            .Where(source => !string.Equals(source.Word, replacement, StringComparison.OrdinalIgnoreCase))
            .Where(source => source.EditCost <= minimumCost + 0.01)
            .Where(source => source.Frequency >= frequency - 0.12)
            .ToList();

        if (sameCostCompetitors.Count > 0)
        {
            return false;
        }

        var competitors = analysis.Sources
            .Where(source => !string.Equals(source.Word, replacement, StringComparison.OrdinalIgnoreCase))
            .Where(source => source.EditCost <= analysis.Sources.Min(s => s.EditCost) + NearEqualEditGap)
            .ToList();

        if (competitors.Count == 0)
        {
            return true;
        }

        return competitors.All(source => frequency >= source.Frequency + ClearFrequencyMargin)
            || (frequency >= 0.995 && competitors.All(source => source.Frequency <= frequency - 0.004));
    }
}
