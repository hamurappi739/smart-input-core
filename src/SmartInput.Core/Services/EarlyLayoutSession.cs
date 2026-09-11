using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

// Text-bearing records are transient commands, never diagnostic log entries.
public sealed class EarlyLayoutProposal
{
    public required long Version { get; init; }
    public required nint Context { get; init; }
    public required string Original { get; init; }
    public required string Replacement { get; init; }
    public required TypingLanguage TargetLanguage { get; init; }
}

public readonly record struct EarlyLayoutSessionStatus(long Version, int Length, bool Switched, bool Reserved);

/// <summary>
/// Single-owner token transaction state. The adapter must reserve its input
/// barrier before TryReserve and keep it until Complete. Observed input, a focus
/// change or a policy change invalidates outstanding proposals. This class does
/// not inject text or switch the OS layout.
/// </summary>
public sealed class EarlyLayoutSession
{
    private readonly EarlyLayoutModel _model;
    private readonly EarlyLayoutOptions _options;
    private readonly KeyboardLayoutConverter _converter = new();
    private string _token = string.Empty;
    private nint _context;
    private TypingLanguage _language;
    private bool _allowed;
    private bool _switched;
    private bool _reserved;
    private bool _protectedToken;
    private long _version;
    private EarlyLayoutProposal? _proposal;

    public EarlyLayoutSession(EarlyLayoutModel model, EarlyLayoutOptions? options = null)
    {
        _model = model;
        _options = options ?? new();
    }

    public EarlyLayoutSessionStatus Status => new(_version, _token.Length, _switched, _reserved);

    public void SetContext(nint context, TypingLanguage language, bool allowed)
    {
        if (context != _context || language != _language || allowed != _allowed)
            Reset();
        _context = context;
        _language = language;
        _allowed = allowed && context != 0;
    }

    public EarlyLayoutProposal? Append(char character)
    {
        if (_reserved) throw new InvalidOperationException("Input must remain behind the adapter barrier.");
        _version++;
        _proposal = null;
        if (!_allowed || _protectedToken) return null;
        if (!char.IsLetter(character) || _token.Length >= 128)
        {
            _protectedToken = true;
            _token = string.Empty;
            return null;
        }
        _token += character;
        if (_switched) return null;
        var decision = _model.Evaluate(_token, _language, _options);
        if (decision.Verdict != EarlyLayoutVerdict.Candidate) return null;
        _proposal = new EarlyLayoutProposal
        {
            Version = _version,
            Context = _context,
            Original = _token,
            Replacement = _converter.Convert(_token, _language == TypingLanguage.English
                ? LayoutConversionDirection.EnglishToRussian : LayoutConversionDirection.RussianToEnglish),
            TargetLanguage = decision.TargetLanguage,
        };
        return _proposal;
    }

    public void Backspace()
    {
        if (_reserved) throw new InvalidOperationException("Input must remain behind the adapter barrier.");
        _version++;
        _proposal = null;
        if (_token.Length > 0) _token = _token[..^1];
        // Editing after a switch does not re-arm automatic switching.
    }

    public bool TryReserve(EarlyLayoutProposal proposal, nint currentContext, bool inputBarrierOwned)
    {
        if (!inputBarrierOwned || !_allowed || _reserved || _switched
            || !ReferenceEquals(proposal, _proposal) || proposal.Version != _version
            || currentContext != _context || proposal.Original != _token)
            return false;
        _reserved = true;
        return true;
    }

    public bool Complete(EarlyLayoutProposal proposal, bool textApplied, bool layoutAcknowledged)
    {
        if (!_reserved || !ReferenceEquals(proposal, _proposal) || proposal.Version != _version)
            return false;
        _reserved = false;
        _proposal = null;
        _version++;
        // Never retry the same word after a failed or partially applied transaction.
        _switched = true;
        if (textApplied && layoutAcknowledged)
        {
            _token = proposal.Replacement;
            _language = proposal.TargetLanguage;
            return true;
        }
        _token = string.Empty;
        return false;
    }

    public void Reset()
    {
        _version++;
        _token = string.Empty;
        _proposal = null;
        _reserved = false;
        _switched = false;
        _protectedToken = false;
    }
}
