using SmartInput.Core.Models;
using SmartInput.Core.Persistence;

namespace SmartInput.Infrastructure.Persistence;

public interface ICorrectionRejectionLearningPersistence
{
    Task<IReadOnlyList<CorrectionRejectionLearningEntry>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        IReadOnlyList<CorrectionRejectionLearningEntry> entries,
        CancellationToken cancellationToken = default);
}
