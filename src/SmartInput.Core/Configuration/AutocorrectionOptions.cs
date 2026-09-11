namespace SmartInput.Core.Configuration;

public sealed class AutocorrectionOptions
{
    public const double DefaultCandidateThreshold = 0.70;

    public const double DefaultWaitThreshold = 0.45;

    public const double DefaultValidWordPlausibilityThreshold = 0.68;

    public const int DefaultMinCandidateTokenLength = 3;

    public const int DefaultMaxGeneratedCandidates = 512;

    public const double DefaultAmbiguousCandidateScoreGap = 0.05;

    public const int DefaultMaxSubstitutionTokenLength = 8;

    public const int DefaultMaxExternalEditDistance = 2;

    public double CandidateThreshold { get; init; } = DefaultCandidateThreshold;

    public double WaitThreshold { get; init; } = DefaultWaitThreshold;

    public double ValidWordPlausibilityThreshold { get; init; } = DefaultValidWordPlausibilityThreshold;

    public int MinCandidateTokenLength { get; init; } = DefaultMinCandidateTokenLength;

    public int MaxGeneratedCandidates { get; init; } = DefaultMaxGeneratedCandidates;

    public double AmbiguousCandidateScoreGap { get; init; } = DefaultAmbiguousCandidateScoreGap;

    public int MaxSubstitutionTokenLength { get; init; } = DefaultMaxSubstitutionTokenLength;

    /// <summary>
    /// Maximum edit distance accepted from the local spelling provider. A
    /// distance of two is still subject to the high-frequency and apply gates.
    /// </summary>
    public int MaxExternalEditDistance { get; init; } = DefaultMaxExternalEditDistance;

    /// <summary>
    /// Enables the local Hunspell/SymSpell adapter for an explicit comparison
    /// run. It remains disabled for the normal live pipeline by default.
    /// </summary>
    public bool UseExternalProvider { get; init; }
}
