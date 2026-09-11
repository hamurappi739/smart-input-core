namespace SmartInput.Core.Configuration;

public sealed class WrongLayoutDetectionOptions
{
    public const double DefaultCandidateThreshold = 0.72;

    public const double DefaultWaitThreshold = 0.40;

    public const double UnknownLatinNameCandidateThreshold = 0.73;

    public const double UnknownLatinNameMaxConfidence = 0.88;

    public double CandidateThreshold { get; init; } = DefaultCandidateThreshold;

    public double WaitThreshold { get; init; } = DefaultWaitThreshold;
}
