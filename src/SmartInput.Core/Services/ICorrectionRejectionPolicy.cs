using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

public interface ICorrectionRejectionPolicy
{
    int SuppressionThreshold { get; }

    bool IsAutomaticallySuppressed(string candidate, string replacement, CorrectionKind kind);

    Task AllowAgainAsync(
        string candidate,
        string replacement,
        CorrectionKind kind,
        CancellationToken cancellationToken = default);
}

public sealed class CorrectionRejectionPolicy : ICorrectionRejectionPolicy
{
    public const int DefaultSuppressionThreshold = 2;

    private readonly Persistence.ICorrectionRejectionLearningStore _learningStore;

    public CorrectionRejectionPolicy(Persistence.ICorrectionRejectionLearningStore learningStore)
    {
        _learningStore = learningStore;
    }

    public int SuppressionThreshold => DefaultSuppressionThreshold;

    public bool IsAutomaticallySuppressed(string candidate, string replacement, CorrectionKind kind)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(replacement))
        {
            return false;
        }

        return _learningStore.GetUndoCount(candidate, replacement, kind) >= SuppressionThreshold;
    }

    public Task AllowAgainAsync(
        string candidate,
        string replacement,
        CorrectionKind kind,
        CancellationToken cancellationToken = default)
    {
        return _learningStore.RemoveAsync(candidate, replacement, kind, cancellationToken);
    }
}

public sealed class NullCorrectionRejectionPolicy : ICorrectionRejectionPolicy
{
    public static NullCorrectionRejectionPolicy Instance { get; } = new();

    public int SuppressionThreshold => int.MaxValue;

    public bool IsAutomaticallySuppressed(string candidate, string replacement, CorrectionKind kind) => false;

    public Task AllowAgainAsync(
        string candidate,
        string replacement,
        CorrectionKind kind,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
