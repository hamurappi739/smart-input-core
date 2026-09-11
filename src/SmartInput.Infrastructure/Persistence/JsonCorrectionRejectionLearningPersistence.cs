using Microsoft.Extensions.Logging;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;

namespace SmartInput.Infrastructure.Persistence;

public sealed class JsonCorrectionRejectionLearningPersistence : ICorrectionRejectionLearningPersistence
{
    private readonly string _learningFilePath;
    private readonly ILogger<JsonCorrectionRejectionLearningPersistence> _logger;

    public JsonCorrectionRejectionLearningPersistence(
        ILogger<JsonCorrectionRejectionLearningPersistence> logger)
        : this(GetDefaultLearningPath(), logger)
    {
    }

    public JsonCorrectionRejectionLearningPersistence(
        string learningFilePath,
        ILogger<JsonCorrectionRejectionLearningPersistence> logger)
    {
        _learningFilePath = learningFilePath;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CorrectionRejectionLearningEntry>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await AtomicJsonFile
                .ReadAsync(
                    _learningFilePath,
                    SmartInputPersistenceJsonContext.Default.CorrectionRejectionLearningDocument,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Correction rejection learning data loaded from local storage.");
            return document?.Entries ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            _logger.LogWarning("Correction rejection learning file was malformed and will be ignored.");
            return [];
        }
        catch (InvalidDataException)
        {
            _logger.LogWarning("Correction rejection learning file exceeded the local safety limit and will be ignored.");
            return [];
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read correction rejection learning data.");
            return [];
        }

    }

    public async Task SaveAsync(
        IReadOnlyList<CorrectionRejectionLearningEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var document = new CorrectionRejectionLearningDocument
        {
            Entries = entries.ToList(),
        };

        await AtomicJsonFile
            .WriteAsync(
                _learningFilePath,
                document,
                SmartInputPersistenceJsonContext.Default.CorrectionRejectionLearningDocument,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Correction rejection learning data saved to local storage.");
    }

    public static string GetDefaultLearningPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SmartInput", "correction-rejection-learning.json");
    }

    internal sealed class CorrectionRejectionLearningDocument
    {
        public List<CorrectionRejectionLearningEntry> Entries { get; init; } = [];
    }
}
