using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Services;

/// <summary>
/// Shared hand-off between the synchronous Windows hook preflight and the
/// serialized C# live correction pipeline. It owns no injection and never
/// writes diagnostics containing token text.
/// </summary>
public interface IEarlyLayoutCorrectionService
{
    bool HasPendingEarlyLayoutCorrection { get; }

    event Action<EarlyLayoutCorrectionApplied>? EarlyLayoutApplied;

    PreparedLayoutCorrection? ObserveCharacter(
        nint context,
        char character,
        TypingLanguage? sourceLanguage,
        bool allowed);

    bool TryReserve(
        PreparedLayoutCorrection correction,
        nint currentContext,
        bool inputBarrierOwned);

    bool Complete(
        PreparedLayoutCorrection correction,
        bool textApplied,
        bool layoutAcknowledged);

    bool TryBuildUndoOriginal(
        string correctedToken,
        nint currentContext,
        out string originalToken);

    void Backspace();

    /// <summary>
    /// Ends the hook-side token transaction at a boundary while preserving a
    /// successful early prefix long enough for the serialized engine to
    /// create the complete-word Undo transaction.
    /// </summary>
    void PrepareForBoundary();

    void Reset();
}

public sealed record EarlyLayoutCorrectionApplied(
    nint Context,
    string OriginalPrefix,
    string ReplacementPrefix);

public sealed class EarlyLayoutCorrectionService : IEarlyLayoutCorrectionService
{
    private readonly EarlyLayoutSession _session;
    private readonly KeyboardLayoutConverter _converter = new();
    private readonly object _sync = new();

    private EarlyLayoutProposal? _pendingProposal;
    private nint _successfulContext;
    private TypingLanguage _successfulSourceLanguage;
    private string _successfulReplacementPrefix = string.Empty;
    private bool _hasSuccessfulCorrection;

    public EarlyLayoutCorrectionService(EarlyLayoutSession session)
    {
        _session = session;
    }

    public bool HasPendingEarlyLayoutCorrection
    {
        get
        {
            lock (_sync)
            {
                return _pendingProposal is not null;
            }
        }
    }

    public event Action<EarlyLayoutCorrectionApplied>? EarlyLayoutApplied;

    public PreparedLayoutCorrection? ObserveCharacter(
        nint context,
        char character,
        TypingLanguage? sourceLanguage,
        bool allowed)
    {
        lock (_sync)
        {
            if (!allowed
                || context == 0
                || sourceLanguage is not (TypingLanguage.English or TypingLanguage.Russian))
            {
                _pendingProposal = null;
                _session.Reset();
                return null;
            }

            _session.SetContext(context, sourceLanguage.Value, allowed);
            // A new character supersedes any proposal that was not reserved
            // by the serialized adapter. This also disarms an early proposal
            // when the next key is a code/identifier separator.
            _pendingProposal = null;
            var proposal = _session.Append(character);
            if (proposal is null)
            {
                return null;
            }

            _pendingProposal = proposal;
            return ToPreparedCorrection(proposal);
        }
    }

    public bool TryReserve(
        PreparedLayoutCorrection correction,
        nint currentContext,
        bool inputBarrierOwned)
    {
        lock (_sync)
        {
            var reserved = _pendingProposal is not null
                && Matches(_pendingProposal, correction)
                && _session.TryReserve(_pendingProposal, currentContext, inputBarrierOwned);
            if (!reserved)
            {
                // A proposal that missed its FIFO/context window must never
                // keep the Windows monitor in a pending-input barrier.
                _pendingProposal = null;
                _session.Reset();
            }

            return reserved;
        }
    }

    public bool Complete(
        PreparedLayoutCorrection correction,
        bool textApplied,
        bool layoutAcknowledged)
    {
        EarlyLayoutCorrectionApplied? applied = null;
        bool completed;

        lock (_sync)
        {
            if (_pendingProposal is null || !Matches(_pendingProposal, correction))
            {
                _pendingProposal = null;
                _session.Reset();
                return false;
            }

            var proposal = _pendingProposal;
            completed = _session.Complete(proposal, textApplied, layoutAcknowledged);
            _pendingProposal = null;

            if (completed)
            {
                _successfulContext = proposal.Context;
                _successfulSourceLanguage = proposal.TargetLanguage == TypingLanguage.Russian
                    ? TypingLanguage.English
                    : TypingLanguage.Russian;
                _successfulReplacementPrefix = proposal.Replacement;
                _hasSuccessfulCorrection = true;
                applied = new EarlyLayoutCorrectionApplied(
                    proposal.Context,
                    proposal.Original,
                    proposal.Replacement);
            }
            else
            {
                _hasSuccessfulCorrection = false;
                _successfulReplacementPrefix = string.Empty;
            }
        }

        if (applied is not null)
        {
            try
            {
                EarlyLayoutApplied?.Invoke(applied);
            }
            catch
            {
                // Observers reconcile their preflight view only. A callback
                // failure must not turn a completed text/layout transaction
                // into an unhandled hook-pipeline exception.
            }
        }

        return completed;
    }

    public bool TryBuildUndoOriginal(
        string correctedToken,
        nint currentContext,
        out string originalToken)
    {
        lock (_sync)
        {
            originalToken = string.Empty;
            if (!_hasSuccessfulCorrection
                || currentContext == 0
                || currentContext != _successfulContext
                || string.IsNullOrEmpty(correctedToken)
                || !correctedToken.StartsWith(
                    _successfulReplacementPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var direction = _successfulSourceLanguage == TypingLanguage.English
                ? LayoutConversionDirection.RussianToEnglish
                : LayoutConversionDirection.EnglishToRussian;
            originalToken = _converter.Convert(correctedToken, direction);
            return !string.IsNullOrEmpty(originalToken)
                && !string.Equals(
                    originalToken,
                    correctedToken,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Backspace()
    {
        lock (_sync)
        {
            if (_pendingProposal is not null)
            {
                _pendingProposal = null;
            }

            _session.Backspace();
        }
    }

    public void PrepareForBoundary()
    {
        lock (_sync)
        {
            _pendingProposal = null;
            _session.Reset();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _pendingProposal = null;
            _hasSuccessfulCorrection = false;
            _successfulContext = 0;
            _successfulReplacementPrefix = string.Empty;
            _session.Reset();
        }
    }

    private static bool Matches(
        EarlyLayoutProposal proposal,
        PreparedLayoutCorrection correction)
    {
        return proposal.Context != 0
            && string.Equals(proposal.Original, correction.OriginalText, StringComparison.Ordinal)
            && string.Equals(proposal.Replacement, correction.ReplacementText, StringComparison.Ordinal)
            && ((proposal.TargetLanguage == TypingLanguage.Russian
                    && correction.TargetInputLanguage == KeyboardInputLanguage.Russian)
                || (proposal.TargetLanguage == TypingLanguage.English
                    && correction.TargetInputLanguage == KeyboardInputLanguage.English));
    }

    private static PreparedLayoutCorrection ToPreparedCorrection(EarlyLayoutProposal proposal)
    {
        return new PreparedLayoutCorrection
        {
            OriginalText = proposal.Original,
            ReplacementText = proposal.Replacement,
            TargetInputLanguage = proposal.TargetLanguage == TypingLanguage.Russian
                ? KeyboardInputLanguage.Russian
                : KeyboardInputLanguage.English,
        };
    }
}
