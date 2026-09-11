using Microsoft.Extensions.Logging;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;

namespace SmartInput.Infrastructure.Persistence;

public sealed class JsonUserAutocorrectDictionaryPersistence : IUserAutocorrectDictionaryPersistence
{
    private readonly string _dictionaryFilePath;
    private readonly ILogger<JsonUserAutocorrectDictionaryPersistence> _logger;

    public JsonUserAutocorrectDictionaryPersistence(ILogger<JsonUserAutocorrectDictionaryPersistence> logger)
        : this(GetDefaultDictionaryPath(), logger)
    {
    }

    public JsonUserAutocorrectDictionaryPersistence(string dictionaryFilePath, ILogger<JsonUserAutocorrectDictionaryPersistence> logger)
    {
        _dictionaryFilePath = dictionaryFilePath;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserAutocorrectDictionaryEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await AtomicJsonFile
                .ReadAsync(
                    _dictionaryFilePath,
                    SmartInputPersistenceJsonContext.Default.UserAutocorrectDictionaryDocument,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("User autocorrect dictionary loaded from local storage.");
            return document?.Entries ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            _logger.LogWarning("User autocorrect dictionary file was malformed and will be ignored.");
            return [];
        }
        catch (InvalidDataException)
        {
            _logger.LogWarning("User autocorrect dictionary file exceeded the local safety limit and will be ignored.");
            return [];
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read user autocorrect dictionary.");
            return [];
        }

    }

    public async Task SaveAsync(
        IReadOnlyList<UserAutocorrectDictionaryEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var document = new UserAutocorrectDictionaryDocument
        {
            Entries = entries.ToList(),
        };

        await AtomicJsonFile
            .WriteAsync(
                _dictionaryFilePath,
                document,
                SmartInputPersistenceJsonContext.Default.UserAutocorrectDictionaryDocument,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("User autocorrect dictionary saved to local storage.");
    }

    public static string GetDefaultDictionaryPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SmartInput", "user-autocorrect-dictionary.json");
    }

    internal sealed class UserAutocorrectDictionaryDocument
    {
        public List<UserAutocorrectDictionaryEntry> Entries { get; init; } = [];
    }
}
