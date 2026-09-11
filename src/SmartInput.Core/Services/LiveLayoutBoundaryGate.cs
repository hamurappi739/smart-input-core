using System.Text;
using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.Core.Services;

public sealed class LiveLayoutBoundaryGate : ILiveLayoutBoundaryGate
{
    private readonly IAutomaticLayoutCorrectionEngine _correctionEngine;
    private readonly ISettingsService _settingsService;
    private readonly IEmergencyPauseService _emergencyPauseService;
    private readonly ITextReplacementSessionNotifier _replacementSessionNotifier;
    private readonly IWrongLayoutDetectionService? _wrongLayoutDetectionService;
    private readonly IAutocorrectDictionary? _dictionary;
    private readonly IEarlyLayoutCorrectionService? _earlyLayoutCorrectionService;
    private readonly SentenceLanguageContextBuffer? _sentenceLanguageContext;
    private readonly StringBuilder _preflightToken = new();
    private readonly StringBuilder _preflightSnippetToken = new();
    private readonly object _preflightSync = new();
    private CancellationTokenSource? _layoutPreflightCancellation;
    private string _layoutPreflightToken = string.Empty;
    private PreparedLayoutCorrection? _layoutPreflightCandidate;
    private bool _layoutPreflightReady;
    private int _layoutPreflightGeneration;
    private bool _suppressCurrentBoundary;
    private string? _completedToken;
    private bool _possibleEnglishLayoutCommaPrefix;
    private bool _previousBoundaryWasWhitespace;
    private nint _preflightWindowHandle;

    public LiveLayoutBoundaryGate(
        IAutomaticLayoutCorrectionEngine correctionEngine,
        ISettingsService settingsService,
        IEmergencyPauseService emergencyPauseService,
        ITextReplacementSessionNotifier replacementSessionNotifier,
        IWrongLayoutDetectionService? wrongLayoutDetectionService = null,
        IAutocorrectDictionary? dictionary = null,
        IEarlyLayoutCorrectionService? earlyLayoutCorrectionService = null,
        SentenceLanguageContextBuffer? sentenceLanguageContext = null)
    {
        _correctionEngine = correctionEngine;
        _settingsService = settingsService;
        _emergencyPauseService = emergencyPauseService;
        _replacementSessionNotifier = replacementSessionNotifier;
        _wrongLayoutDetectionService = wrongLayoutDetectionService;
        _dictionary = dictionary;
        _earlyLayoutCorrectionService = earlyLayoutCorrectionService;
        _sentenceLanguageContext = sentenceLanguageContext;
        if (_earlyLayoutCorrectionService is not null)
        {
            _earlyLayoutCorrectionService.EarlyLayoutApplied += OnEarlyLayoutApplied;
        }
    }

    public bool ShouldSuppressPendingBoundary()
    {
        lock (_preflightSync)
        {
            if (_suppressCurrentBoundary)
            {
                _suppressCurrentBoundary = false;
                return true;
            }
        }

        if (_replacementSessionNotifier.IsReplacementActive)
        {
            return false;
        }

        if (!_correctionEngine.HasPendingLiveWork)
        {
            return false;
        }

        if (!_settingsService.Current.IsEnabled)
        {
            return false;
        }

        var settings = _settingsService.Current;
        var letterPending = _correctionEngine.PendingTokenLength > 0;
        var snippetPending = _correctionEngine.PendingSnippetTriggerLength > 0;

        var hasEnabledFeature =
            (letterPending && (settings.AutomaticLayoutEnabled || settings.AutocorrectEnabled))
            || (snippetPending && settings.SnippetsEnabled);

        if (!hasEnabledFeature)
        {
            return false;
        }

        if (_emergencyPauseService.IsPaused)
        {
            return false;
        }

        if (_correctionEngine.CachedPolicyState != AutomationPolicyState.Allowed)
        {
            return false;
        }

        // Candidates are created by ObserveKeyDown. The interceptor reads them
        // immediately and suppresses only that one boundary; all other text
        // continues directly to the target application.
        return false;
    }

    public void ObserveApplicationContext(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return;
        }

        lock (_preflightSync)
        {
            if (_preflightWindowHandle == 0)
            {
                _preflightWindowHandle = windowHandle;
                return;
            }

            if (_preflightWindowHandle == windowHandle)
            {
                return;
            }

            _preflightWindowHandle = windowHandle;
            ClearPendingTokenLocked();
        }
    }

    public PreparedLayoutCorrection? ObserveKeyDown(KeyboardCharacterResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        lock (_preflightSync)
        {
            switch (resolution.Kind)
            {
                case CharacterResolutionKind.Character:
                    if (_possibleEnglishLayoutCommaPrefix
                        && TokenScriptAnalyzer.IsLatinLetter(resolution.Character))
                    {
                        _preflightToken.Append(',');
                    }

                    _possibleEnglishLayoutCommaPrefix = false;
                    _previousBoundaryWasWhitespace = false;

                    if (char.IsLetter(resolution.Character)
                        || CanAppendEnglishLayoutPunctuation(resolution.Character))
                    {
                        if (_preflightToken.Length < CurrentTokenBufferOptions.DefaultMaxTokenLength)
                        {
                            _preflightToken.Append(resolution.Character);
                        }
                        else
                        {
                            _preflightToken.Clear();
                        }
                    }
                    else
                    {
                        _preflightToken.Clear();
                    }

                    var earlyCorrection = TryPrepareEarlyLayoutCorrectionLocked(
                        resolution.Character);

                    if (InputCharacterClassification.IsTrackableTriggerCharacter(resolution.Character))
                    {
                        if (_preflightSnippetToken.Length < SnippetTriggerBufferOptions.DefaultMaxTriggerLength)
                        {
                            _preflightSnippetToken.Append(resolution.Character);
                        }
                        else
                        {
                            _preflightSnippetToken.Clear();
                        }
                    }
                    else
                    {
                        _preflightSnippetToken.Clear();
                    }

                    ScheduleLayoutPreflightLocked();
                    return earlyCorrection;

                case CharacterResolutionKind.Backspace:
                    _possibleEnglishLayoutCommaPrefix = false;
                    _previousBoundaryWasWhitespace = false;
                    if (_preflightToken.Length == 0)
                    {
                        _preflightToken.Clear();
                    }
                    else
                    {
                        _preflightToken.Length--;
                    }

                    if (_preflightSnippetToken.Length > 0)
                    {
                        _preflightSnippetToken.Length--;
                    }

                    ScheduleLayoutPreflightLocked();
                    return null;

                case CharacterResolutionKind.Reset:
                case CharacterResolutionKind.Uncertain:
                    _preflightToken.Clear();
                    _preflightSnippetToken.Clear();
                    _possibleEnglishLayoutCommaPrefix = false;
                    _previousBoundaryWasWhitespace = false;
                    InvalidateLayoutPreflightLocked();
                    return null;

                case CharacterResolutionKind.WordBoundary:
                    if (TryHandlePotentialEnglishLayoutBoundaryLocked(
                            resolution.Boundary,
                            out var punctuationBoundaryCorrection))
                    {
                        return punctuationBoundaryCorrection;
                    }

                    var hadPreflightToken = _preflightToken.Length > 0;
                    var prepared = TakePreparedCorrectionAtBoundary(resolution.Boundary);
                    // At the beginning of a buffer there is no preceding
                    // whitespace event yet, but a leading comma can still be
                    // the Russian б key under the English layout. Keep a
                    // bounded possibility until the next character/boundary;
                    // if it is ordinary punctuation, no candidate is formed
                    // and the preflight is discarded normally.
                    _possibleEnglishLayoutCommaPrefix = !hadPreflightToken
                        && IsCommaBoundary(resolution.Boundary);
                    _previousBoundaryWasWhitespace = IsWhitespaceBoundary(resolution.Boundary);
                    return prepared;

                default:
                    return null;
            }
        }
    }

    public string? TakeCompletedToken()
    {
        lock (_preflightSync)
        {
            var completedToken = _completedToken;
            _completedToken = null;
            return completedToken;
        }
    }

    public void ResetPendingToken()
    {
        lock (_preflightSync)
        {
            ClearPendingTokenLocked();
            _preflightWindowHandle = 0;
        }
    }

    public bool HasPendingEarlyLayoutCorrection()
        => _earlyLayoutCorrectionService?.HasPendingEarlyLayoutCorrection == true;

    private void ClearPendingTokenLocked()
    {
        _preflightToken.Clear();
        _preflightSnippetToken.Clear();
        _suppressCurrentBoundary = false;
        _completedToken = null;
        _possibleEnglishLayoutCommaPrefix = false;
        _previousBoundaryWasWhitespace = false;
        InvalidateLayoutPreflightLocked();
        _earlyLayoutCorrectionService?.Reset();
    }

    private PreparedLayoutCorrection? TryPrepareEarlyLayoutCorrectionLocked(char character)
    {
        if (_earlyLayoutCorrectionService is null
            || !IsSafeAutomaticLayoutContext()
            || !char.IsLetter(character)
            || _preflightToken.Length > EarlyLayoutModel.MaximumPrefixLength)
        {
            if (!char.IsLetter(character))
            {
                _earlyLayoutCorrectionService?.Reset();
            }

            return null;
        }

        var sourceLanguage = TokenScriptAnalyzer.Classify(_preflightToken.ToString()) switch
        {
            TokenScript.Latin => TypingLanguage.English,
            TokenScript.Cyrillic => TypingLanguage.Russian,
            _ => (TypingLanguage?)null,
        };

        return _earlyLayoutCorrectionService.ObserveCharacter(
            _preflightWindowHandle,
            character,
            sourceLanguage,
            allowed: true);
    }

    private void OnEarlyLayoutApplied(EarlyLayoutCorrectionApplied applied)
    {
        lock (_preflightSync)
        {
            var current = _preflightToken.ToString();
            if (!current.EndsWith(applied.OriginalPrefix, StringComparison.Ordinal))
            {
                return;
            }

            _preflightToken.Remove(
                _preflightToken.Length - applied.OriginalPrefix.Length,
                applied.OriginalPrefix.Length);
            _preflightToken.Append(applied.ReplacementPrefix);
            ScheduleLayoutPreflightLocked();
        }
    }

    private PreparedLayoutCorrection? TakePreparedCorrectionAtBoundary(DeferredBoundaryKey? boundary)
    {
        var token = _preflightToken.ToString();
        var snippetTrigger = _preflightSnippetToken.ToString();
        var layoutPreflightReady = _layoutPreflightReady
            && string.Equals(_layoutPreflightToken, token, StringComparison.Ordinal);
        var layoutPreflightCandidate = layoutPreflightReady
            ? _layoutPreflightCandidate
            : null;
        _completedToken = IsLetterToken(token) ? token : null;
        _preflightToken.Clear();
        _preflightSnippetToken.Clear();
        InvalidateLayoutPreflightLocked();
        _earlyLayoutCorrectionService?.PrepareForBoundary();

        _suppressCurrentBoundary = ShouldSuppressBoundary(
            token,
            snippetTrigger,
            boundary);

        if (string.IsNullOrEmpty(token)
            || !IsSafeAutomaticLayoutContext()
            || IsUnsupportedBoundary(boundary))
        {
            return null;
        }

        if (LayoutServiceWordWhitelist.TryGetCommaPrefixedRussianReplacement(
                token,
                out var commaPrefixedReplacement))
        {
            return new PreparedLayoutCorrection
            {
                OriginalText = token,
                ReplacementText = commaPrefixedReplacement,
                TargetInputLanguage = KeyboardInputLanguage.Russian,
            };
        }

        if (TryPrepareInternalEnglishLayoutToken(token, out var internalLayoutCorrection))
        {
            return internalLayoutCorrection;
        }

        // The detector is intentionally cancellable and runs off the hook
        // thread. A very fast token + boundary can arrive before that worker
        // finishes, so keep a tiny synchronous dictionary path for clear
        // layout pairs. It does not wait, log text, or bypass safety gates.
        if (TryPrepareFastDictionaryLayout(token, out var fastLayoutCorrection))
        {
            return fastLayoutCorrection;
        }

        // The potentially expensive detector runs in the bounded preflight
        // worker while the user types.  If its snapshot is not ready for this
        // exact token, fail open here; Core's asynchronous spelling/layout
        // path may still process a boundary that was held for spelling.
        if (layoutPreflightReady)
        {
            return layoutPreflightCandidate;
        }

        return null;
    }

    private void ScheduleLayoutPreflightLocked()
    {
        InvalidateLayoutPreflightLocked();

        // With the production dictionary loaded, exact layout targets are
        // already handled synchronously by TryPrepareFastDictionaryLayout at
        // the boundary, while EarlyLayoutSession owns the four-key path.
        // Starting and cancelling a Task.Run for every character duplicated
        // that work and could build a ThreadPool backlog during fast typing.
        // Keep the asynchronous heuristic only as the dictionary-less
        // fallback used by reduced/test hosts.
        if (_dictionary is not null)
        {
            return;
        }

        if (_wrongLayoutDetectionService is null
            || _preflightToken.Length < 3
            || !IsLetterOrInternalCommaToken(_preflightToken))
        {
            return;
        }

        var token = _preflightToken.ToString();
        var generation = _layoutPreflightGeneration;
        var cancellation = new CancellationTokenSource();
        _layoutPreflightCancellation = cancellation;
        _layoutPreflightToken = token;

        _ = PrepareLayoutPreflightAsync(token, generation, cancellation.Token);
    }

    private async Task PrepareLayoutPreflightAsync(
        string token,
        int generation,
        CancellationToken cancellationToken)
    {
        PreparedLayoutCorrection? candidate;
        try
        {
            candidate = await Task.Run(
                    () => EvaluateLayoutCandidate(token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            // A background preflight is an optimization only.  The live path
            // must remain fail-open if the worker cannot evaluate a snapshot.
            return;
        }

        lock (_preflightSync)
        {
            if (generation != _layoutPreflightGeneration
                || !string.Equals(_layoutPreflightToken, token, StringComparison.Ordinal)
                || !string.Equals(_preflightToken.ToString(), token, StringComparison.Ordinal))
            {
                return;
            }

            _layoutPreflightCandidate = candidate;
            _layoutPreflightReady = true;
        }
    }

    private PreparedLayoutCorrection? EvaluateLayoutCandidate(string token)
    {
        if (string.IsNullOrEmpty(token)
            || !IsSafeAutomaticLayoutContext())
        {
            return null;
        }

        if (LayoutServiceWordWhitelist.TryGetCommaPrefixedRussianReplacement(
                token,
                out var commaPrefixedReplacement))
        {
            return new PreparedLayoutCorrection
            {
                OriginalText = token,
                ReplacementText = commaPrefixedReplacement,
                TargetInputLanguage = KeyboardInputLanguage.Russian,
            };
        }

        if (TryPrepareInternalEnglishLayoutToken(token, out var internalLayoutCorrection))
        {
            return internalLayoutCorrection;
        }

        if (TryPrepareFastDictionaryLayout(token, out var fastLayoutCorrection))
        {
            return fastLayoutCorrection;
        }

        // An exact source-language word cannot be a wrong-layout candidate.
        // Spelling suppression remains independent and is decided at the
        // boundary by ShouldSuppressBoundary.
        if (_dictionary is not null
            && TrustedWordAnalyzer.ShouldBlockLayoutConversion(token, _dictionary))
        {
            return null;
        }

        var detection = _wrongLayoutDetectionService?.Evaluate(
            token,
            ActiveLanguageSet.EnglishAndRussian);
        if (detection?.Recommendation != LayoutDetectionRecommendation.Candidate
            || detection.ConfidenceScore < WrongLayoutDetectionOptions.DefaultCandidateThreshold
            || string.IsNullOrEmpty(detection.CandidateToken))
        {
            return null;
        }

        // The heuristic detector can produce a plausible-looking conversion
        // for a misspelling.  A layout preflight is allowed only when the
        // bounded apply guard verifies the target word.
        if (_dictionary is not null
            && !BoundedCandidateApplyGuard.AllowsPreparedLayout(
                token,
                detection.CandidateToken,
                _dictionary,
                new KeyboardLayoutConverter(),
                _sentenceLanguageContext?.GetHint()))
        {
            return null;
        }

        return new PreparedLayoutCorrection
        {
            OriginalText = token,
            ReplacementText = detection.CandidateToken,
            TargetInputLanguage = detection.ConversionDirection == LayoutConversionDirection.EnglishToRussian
                ? KeyboardInputLanguage.Russian
                : KeyboardInputLanguage.English,
        };
    }

    private void InvalidateLayoutPreflightLocked()
    {
        _layoutPreflightGeneration++;
        _layoutPreflightCancellation?.Cancel();
        _layoutPreflightCancellation?.Dispose();
        _layoutPreflightCancellation = null;
        _layoutPreflightToken = string.Empty;
        _layoutPreflightCandidate = null;
        _layoutPreflightReady = false;
    }

    private static bool IsLetterOrInternalCommaToken(StringBuilder token)
    {
        var commaCount = 0;
        foreach (var character in token.ToString())
        {
            if (character == ',')
            {
                commaCount++;
                continue;
            }

            if (!TokenScriptAnalyzer.IsLatinLetter(character)
                && !TokenScriptAnalyzer.IsCyrillicLetter(character))
            {
                return false;
            }
        }

        return commaCount <= 1;
    }

    private bool ShouldSuppressBoundary(
        string token,
        string snippetTrigger,
        DeferredBoundaryKey? boundary)
    {
        if (IsUnsupportedBoundary(boundary)
            || !IsSafeAutomaticCorrectionContext())
        {
            return false;
        }

        if (boundary is not null
            && _settingsService.Current.PunctuationEnabled
            && _correctionEngine.CanApplyPunctuationCorrection(boundary))
        {
            return true;
        }

        var settings = _settingsService.Current;
        var hasSnippetWork = !string.IsNullOrEmpty(snippetTrigger)
            && settings.SnippetsEnabled;

        // A layout candidate is returned by TakePreparedCorrectionAtBoundary
        // and suppresses the boundary before this method is reached. For
        // spelling, hold only a token that is not already known in its own
        // language (plus the narrow terminal-soft-sign omission pattern).
        // Known words and protected/identifier-like tokens keep their space on
        // the native path without waiting for a full model decision.
        var hasLikelySpellingWork = settings.AutocorrectEnabled
            && IsLikelySpellingCandidate(token);

        return hasLikelySpellingWork || hasSnippetWork;
    }

    private bool IsLikelySpellingCandidate(string token)
    {
        if (!IsLetterToken(token)
            || token.Length < AutocorrectionOptions.DefaultMinCandidateTokenLength
            || ProtectedTokenAnalyzer.IsProtected(token))
        {
            return false;
        }

        var language = TokenScriptAnalyzer.Classify(token) switch
        {
            TokenScript.Cyrillic => TypingLanguage.Russian,
            TokenScript.Latin => TypingLanguage.English,
            _ => (TypingLanguage?)null,
        };

        if (language is null)
        {
            return false;
        }

        // A missing terminal soft sign can be accepted by morphology as a
        // valid stem, so keep this narrow spelling rule eligible even when the
        // exact source is present in the word-form dictionary.
        if (RussianOrthographyHeuristics.IsLikelyTerminalSoftSignOmission(
                token,
                language.Value))
        {
            return true;
        }

        // If the optional dictionary is unavailable, preserve the previous
        // conservative behaviour: Core still gets a chance to evaluate the
        // token at the boundary.
        return _dictionary is null || !_dictionary.Contains(token, language.Value);
    }

    private bool IsSafeAutomaticLayoutContext()
    {
        return _settingsService.Current.IsEnabled
            && _settingsService.Current.AutomaticLayoutEnabled
            && !_emergencyPauseService.IsPaused
            && !_replacementSessionNotifier.IsReplacementActive
            && _correctionEngine.CachedPolicyState == AutomationPolicyState.Allowed;
    }

    private bool IsSafeAutomaticCorrectionContext()
    {
        return _settingsService.Current.IsEnabled
            && !_emergencyPauseService.IsPaused
            && !_replacementSessionNotifier.IsReplacementActive
            && _correctionEngine.CachedPolicyState == AutomationPolicyState.Allowed;
    }

    private static bool IsUnsupportedBoundary(DeferredBoundaryKey? boundary)
    {
        return boundary?.VirtualKeyCode is VirtualKeys.Tab or VirtualKeys.Return;
    }

    private static bool IsCommaBoundary(DeferredBoundaryKey? boundary)
    {
        return boundary?.DeliveryKind == BoundaryDeliveryKind.UnicodeCharacter
            && boundary.Character == ',';
    }

    private bool TryHandlePotentialEnglishLayoutBoundaryLocked(
        DeferredBoundaryKey? boundary,
        out PreparedLayoutCorrection? correction)
    {
        correction = null;
        if (boundary is not null
            && _settingsService.Current.PunctuationEnabled
            && _correctionEngine.CanApplyPunctuationCorrection(boundary))
        {
            return false;
        }

        if (boundary?.DeliveryKind != BoundaryDeliveryKind.UnicodeCharacter
            || boundary.Character is not char character
            || !TokenScriptAnalyzer.IsEnglishKeyProducingRussianLetter(character)
            || !CanAppendEnglishLayoutPunctuation(character))
        {
            return false;
        }

        // If the letters before this key are already a safe complete layout
        // word, this key is real punctuation (ghbdtn, -> привет,). Correct the
        // word now and replay punctuation as the deferred boundary.
        if (_preflightToken.Length > 0
            && TryPrepareFastDictionaryLayout(
                _preflightToken.ToString(),
                out var prefixCorrection))
        {
            correction = prefixCorrection;
            var prefix = _preflightToken.ToString();
            _completedToken = IsLetterToken(prefix) ? prefix : null;
            _preflightToken.Clear();
            _preflightSnippetToken.Clear();
            InvalidateLayoutPreflightLocked();
            _suppressCurrentBoundary = true;
            return true;
        }

        // Otherwise retain the physical punctuation as part of the possible
        // EN-layout image. It has already reached the target; a later exact
        // correction deletes the complete original token including this key.
        _preflightToken.Append(character);
        _preflightSnippetToken.Clear();
        _possibleEnglishLayoutCommaPrefix = false;
        _previousBoundaryWasWhitespace = false;
        ScheduleLayoutPreflightLocked();
        return true;
    }

    private bool CanAppendEnglishLayoutPunctuation(char character)
    {
        if (!TokenScriptAnalyzer.IsEnglishKeyProducingRussianLetter(character))
        {
            return false;
        }

        return _preflightToken.Length == 0
            || TokenScriptAnalyzer.ClassifyForLayout(_preflightToken.ToString()) == TokenScript.Latin;
    }

    private bool TryPrepareInternalEnglishLayoutToken(
        string token,
        out PreparedLayoutCorrection? correction)
    {
        correction = null;
        if (_dictionary is null
            || TokenScriptAnalyzer.Classify(token) != TokenScript.Other
            || TokenScriptAnalyzer.ClassifyForLayout(token) != TokenScript.Latin
            || !TokenScriptAnalyzer.HasEnglishLayoutPunctuation(token))
        {
            return false;
        }

        var physicalRussian = new KeyboardLayoutConverter()
            .Convert(token, LayoutConversionDirection.EnglishToRussian);
        if (physicalRussian.Any(static character => !TokenScriptAnalyzer.IsCyrillicLetter(character))
            || !_dictionary.Contains(physicalRussian, TypingLanguage.Russian)
            || _dictionary.IsNeverAutocorrect(physicalRussian, TypingLanguage.Russian)
            || !BoundedCandidateApplyGuard.AllowsPreparedLayout(
                token,
                physicalRussian,
                _dictionary,
                new KeyboardLayoutConverter(),
                _sentenceLanguageContext?.GetHint()))
        {
            return false;
        }

        correction = new PreparedLayoutCorrection
        {
            OriginalText = token,
            ReplacementText = physicalRussian,
            TargetInputLanguage = KeyboardInputLanguage.Russian,
        };
        return true;
    }

    private bool TryPrepareFastDictionaryLayout(
        string token,
        out PreparedLayoutCorrection? correction)
    {
        correction = null;

        var hasExplicitLayoutWord = LayoutServiceWordWhitelist.TryGetReplacement(
            token,
            out var configuredReplacement,
            out var configuredDirection);

        if (_dictionary is null
            || token.Length < ShortLayoutCandidatePolicy.MinimumLength
            || (ProtectedTokenAnalyzer.IsProtected(token) && !hasExplicitLayoutWord))
        {
            return false;
        }

        var script = TokenScriptAnalyzer.ClassifyForLayout(token);
        var direction = script switch
        {
            TokenScript.Latin => LayoutConversionDirection.EnglishToRussian,
            TokenScript.Cyrillic => LayoutConversionDirection.RussianToEnglish,
            _ => (LayoutConversionDirection?)null,
        };

        if (direction is null)
        {
            return false;
        }

        var converter = new KeyboardLayoutConverter();
        var candidate = direction == LayoutConversionDirection.RussianToEnglish
            && LayoutCorrectionAnchors.TryGetCanonicalEnglishReplacement(token, out var canonical)
            ? canonical
            : converter.Convert(token, direction.Value);

        if (string.IsNullOrWhiteSpace(candidate)
            || string.Equals(token, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (direction == LayoutConversionDirection.EnglishToRussian
            && !candidate.Any(TokenScriptAnalyzer.IsCyrillicLetter))
        {
            return false;
        }

        if (direction == LayoutConversionDirection.RussianToEnglish
            && !candidate.Any(TokenScriptAnalyzer.IsLatinLetter))
        {
            return false;
        }

        var sourceLanguage = direction == LayoutConversionDirection.EnglishToRussian
            ? TypingLanguage.English
            : TypingLanguage.Russian;
        var targetLanguage = direction == LayoutConversionDirection.EnglishToRussian
            ? TypingLanguage.Russian
            : TypingLanguage.English;

        var sourceKnown = sourceLanguage == TypingLanguage.English
            ? _dictionary.Contains(token, TypingLanguage.English)
            : TrustedWordAnalyzer.IsExactKnownOriginal(token, _dictionary);

        var isExplicitLayoutCandidate =
            (hasExplicitLayoutWord
                && configuredDirection == direction
                && string.Equals(configuredReplacement, candidate, StringComparison.OrdinalIgnoreCase))
            || LayoutCorrectionAnchors.IsPositiveAnchor(token);
        var languageHint = _sentenceLanguageContext?.GetHint();
        var isContextualKnownShortCandidate = sourceKnown
            && ShortLayoutCandidatePolicy.AllowsKnownSourceInContext(
                token,
                candidate,
                sourceLanguage,
                _dictionary,
                languageHint);

        if ((sourceKnown && !isExplicitLayoutCandidate && !isContextualKnownShortCandidate)
            || !_dictionary.Contains(candidate, targetLanguage)
            || _dictionary.IsNeverAutocorrect(candidate, targetLanguage)
            || (token.Length > ShortLayoutCandidatePolicy.MaximumLength
                && _dictionary.GetFrequency(candidate, targetLanguage) < 0.55))
        {
            return false;
        }

        if (!BoundedCandidateApplyGuard.AllowsPreparedLayout(
                token,
                candidate,
                _dictionary,
                converter,
                languageHint))
        {
            return false;
        }

        correction = new PreparedLayoutCorrection
        {
            OriginalText = token,
            ReplacementText = candidate,
            TargetInputLanguage = targetLanguage == TypingLanguage.Russian
                ? KeyboardInputLanguage.Russian
                : KeyboardInputLanguage.English,
        };
        return true;
    }

    private static bool IsWhitespaceBoundary(DeferredBoundaryKey? boundary)
    {
        return boundary?.VirtualKeyCode == VirtualKeys.Space
            || (boundary?.DeliveryKind == BoundaryDeliveryKind.UnicodeCharacter
                && char.IsWhiteSpace(boundary.Character ?? default));
    }

    private static bool IsLetterToken(string token)
    {
        return !string.IsNullOrEmpty(token)
            && token.All(static character => char.IsLetter(character));
    }
}
