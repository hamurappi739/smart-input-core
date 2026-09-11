using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;

namespace SmartInput.Infrastructure.Persistence;

public sealed class JsonSnippetPersistence : ISnippetPersistence
{
    private readonly string _snippetsFilePath;
    private readonly ILogger<JsonSnippetPersistence> _logger;

    public JsonSnippetPersistence(ILogger<JsonSnippetPersistence> logger)
        : this(GetDefaultSnippetsPath(), logger)
    {
    }

    public JsonSnippetPersistence(string snippetsFilePath, ILogger<JsonSnippetPersistence> logger)
    {
        _snippetsFilePath = snippetsFilePath;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SnippetDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_snippetsFilePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_snippetsFilePath);
            var document = await System.Text.Json.JsonSerializer
                .DeserializeAsync(stream, SmartInputPersistenceJsonContext.Default.SnippetDocument, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Snippet definitions loaded from local storage.");
            return (document?.Snippets ?? [])
                .Select(MapFromDto)
                .Where(static snippet => snippet is not null)
                .Cast<SnippetDefinition>()
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            _logger.LogWarning("Snippet definitions file was malformed and will be ignored.");
            return [];
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read snippet definitions file.");
            return [];
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<SnippetDefinition> snippets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snippets);

        var directory = Path.GetDirectoryName(_snippetsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new SnippetDocument
        {
            Snippets = snippets.Select(MapToDto).ToList(),
        };

        await using var stream = File.Create(_snippetsFilePath);
        await System.Text.Json.JsonSerializer
            .SerializeAsync(stream, document, SmartInputPersistenceJsonContext.Default.SnippetDocument, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Snippet definitions saved to local storage.");
    }

    public static string GetDefaultSnippetsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SmartInput", "snippets.json");
    }

    private static SnippetDefinition? MapFromDto(SnippetDefinitionDto dto)
    {
        if (dto is null)
        {
            return null;
        }

        return new SnippetDefinition
        {
            Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
            Trigger = dto.Trigger ?? string.Empty,
            Replacement = dto.Replacement ?? string.Empty,
            Language = dto.Language,
            CaseSensitive = dto.CaseSensitive,
            IsEnabled = dto.IsEnabled,
            EnabledApplications = dto.EnabledApplications ?? [],
            DisabledApplications = dto.DisabledApplications ?? [],
        };
    }

    private static SnippetDefinitionDto MapToDto(SnippetDefinition snippet)
    {
        return new SnippetDefinitionDto
        {
            Id = snippet.Id,
            Trigger = snippet.Trigger,
            Replacement = snippet.Replacement,
            Language = snippet.Language,
            CaseSensitive = snippet.CaseSensitive,
            IsEnabled = snippet.IsEnabled,
            EnabledApplications = snippet.EnabledApplications.ToList(),
            DisabledApplications = snippet.DisabledApplications.ToList(),
        };
    }

    internal sealed class SnippetDocument
    {
        public List<SnippetDefinitionDto> Snippets { get; init; } = [];
    }

    internal sealed class SnippetDefinitionDto
    {
        public Guid Id { get; set; }

        public string? Trigger { get; set; }

        public string? Replacement { get; set; }

        public TypingLanguage? Language { get; set; }

        public bool CaseSensitive { get; set; }

        public bool IsEnabled { get; set; } = true;

        public List<string>? EnabledApplications { get; set; }

        public List<string>? DisabledApplications { get; set; }
    }
}
