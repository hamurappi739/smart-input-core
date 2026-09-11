using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Validation;

namespace SmartInput.Core.Services;

public interface ISnippetService
{
    IReadOnlyList<SnippetDefinition> Snippets { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    SnippetValidationResult Validate(SnippetDefinition definition, Guid? excludeId = null);

    Task<SnippetValidationResult> AddAsync(SnippetDefinition definition, CancellationToken cancellationToken = default);

    Task<SnippetValidationResult> UpdateAsync(SnippetDefinition definition, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken = default);

    SnippetMatchResult FindMatch(string triggerText, SnippetMatchContext? context = null);
}

public sealed class SnippetService : ISnippetService
{
    private readonly ISnippetPersistence _persistence;
    private readonly object _sync = new();
    private List<SnippetDefinition> _snippets = [];
    private bool _loaded;

    public SnippetService(ISnippetPersistence persistence)
    {
        _persistence = persistence;
    }

    public IReadOnlyList<SnippetDefinition> Snippets
    {
        get
        {
            lock (_sync)
            {
                return _snippets.ToList();
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _snippets = NormalizeLoaded(loaded);
            _loaded = true;
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SnippetDefinition> snapshot;
        lock (_sync)
        {
            snapshot = _snippets.ToList();
        }

        await _persistence.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    public SnippetValidationResult Validate(SnippetDefinition definition, Guid? excludeId = null)
    {
        lock (_sync)
        {
            return SnippetValidator.Validate(definition, _snippets, excludeId);
        }
    }

    public async Task<SnippetValidationResult> AddAsync(
        SnippetDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        SnippetValidationResult validation;
        lock (_sync)
        {
            var candidate = definition.Id == Guid.Empty
                ? CloneWithId(definition, Guid.NewGuid())
                : definition;
            validation = SnippetValidator.Validate(candidate, _snippets);
            if (!validation.IsValid)
            {
                return validation;
            }

            _snippets.Add(validation.Normalized!);
            _snippets = OrderSnippets(_snippets);
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public async Task<SnippetValidationResult> UpdateAsync(
        SnippetDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        SnippetValidationResult validation;
        lock (_sync)
        {
            if (_snippets.All(snippet => snippet.Id != definition.Id))
            {
                return SnippetValidationResult.Failure("Snippet was not found.");
            }

            validation = SnippetValidator.Validate(definition, _snippets, definition.Id);
            if (!validation.IsValid)
            {
                return validation;
            }

            var index = _snippets.FindIndex(snippet => snippet.Id == definition.Id);
            _snippets[index] = validation.Normalized!;
            _snippets = OrderSnippets(_snippets);
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _snippets.RemoveAll(snippet => snippet.Id == id);
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            var index = _snippets.FindIndex(snippet => snippet.Id == id);
            if (index < 0)
            {
                return;
            }

            var existing = _snippets[index];
            _snippets[index] = new SnippetDefinition
            {
                Id = existing.Id,
                Trigger = existing.Trigger,
                Replacement = existing.Replacement,
                Language = existing.Language,
                CaseSensitive = existing.CaseSensitive,
                IsEnabled = isEnabled,
                EnabledApplications = existing.EnabledApplications,
                DisabledApplications = existing.DisabledApplications,
            };
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public SnippetMatchResult FindMatch(string triggerText, SnippetMatchContext? context = null)
    {
        if (string.IsNullOrEmpty(triggerText))
        {
            return SnippetMatchResult.NoMatch();
        }

        context ??= new SnippetMatchContext();

        List<SnippetDefinition> snapshot;
        lock (_sync)
        {
            snapshot = _snippets.ToList();
        }

        SnippetDefinition? best = null;
        var bestScore = int.MinValue;

        foreach (var snippet in snapshot)
        {
            if (!snippet.IsEnabled)
            {
                continue;
            }

            if (!MatchesLanguage(snippet, context.Language))
            {
                continue;
            }

            if (!MatchesApplication(snippet, context.ApplicationProcessName))
            {
                continue;
            }

            if (!MatchesTrigger(snippet, triggerText))
            {
                continue;
            }

            var score = ScoreSnippet(snippet);
            if (best is null
                || score > bestScore
                || (score == bestScore && CompareTieBreak(snippet, best) < 0))
            {
                best = snippet;
                bestScore = score;
            }
        }

        return best is null
            ? SnippetMatchResult.NoMatch()
            : SnippetMatchResult.Matched(best);
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    private static List<SnippetDefinition> NormalizeLoaded(IReadOnlyList<SnippetDefinition> loaded)
    {
        var normalized = new List<SnippetDefinition>();
        foreach (var entry in loaded)
        {
            var validation = SnippetValidator.Validate(entry, normalized);
            if (!validation.IsValid)
            {
                continue;
            }

            normalized.Add(validation.Normalized!);
        }

        return OrderSnippets(normalized);
    }

    private static List<SnippetDefinition> OrderSnippets(IEnumerable<SnippetDefinition> snippets)
    {
        return snippets
            .OrderBy(snippet => snippet.Trigger, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snippet => snippet.Id)
            .ToList();
    }

    private static bool MatchesLanguage(SnippetDefinition snippet, TypingLanguage? language)
    {
        if (snippet.Language is null)
        {
            return true;
        }

        return language is not null && snippet.Language == language;
    }

    private static bool MatchesApplication(SnippetDefinition snippet, string? applicationProcessName)
    {
        var processName = NormalizeProcessName(applicationProcessName);

        if (snippet.DisabledApplications.Count > 0
            && processName is not null
            && snippet.DisabledApplications.Any(app =>
                string.Equals(app, processName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (snippet.EnabledApplications.Count == 0)
        {
            return true;
        }

        if (processName is null)
        {
            return false;
        }

        return snippet.EnabledApplications.Any(app =>
            string.Equals(app, processName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesTrigger(SnippetDefinition snippet, string triggerText)
    {
        return snippet.CaseSensitive
            ? string.Equals(snippet.Trigger, triggerText, StringComparison.Ordinal)
            : string.Equals(snippet.Trigger, triggerText, StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreSnippet(SnippetDefinition snippet)
    {
        var score = 0;
        if (snippet.EnabledApplications.Count > 0)
        {
            score += 100;
        }

        if (snippet.Language is not null)
        {
            score += 50;
        }

        if (snippet.CaseSensitive)
        {
            score += 25;
        }

        return score;
    }

    private static int CompareTieBreak(SnippetDefinition left, SnippetDefinition right)
    {
        var triggerComparison = string.Compare(left.Trigger, right.Trigger, StringComparison.Ordinal);
        if (triggerComparison != 0)
        {
            return triggerComparison;
        }

        return left.Id.CompareTo(right.Id);
    }

    private static string? NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        return ExcludedApplicationNameValidator.TryNormalizeName(processName, out var normalized, out _)
            ? normalized
            : processName.Trim();
    }

    private static SnippetDefinition CloneWithId(SnippetDefinition definition, Guid id)
    {
        return new SnippetDefinition
        {
            Id = id,
            Trigger = definition.Trigger,
            Replacement = definition.Replacement,
            Language = definition.Language,
            CaseSensitive = definition.CaseSensitive,
            IsEnabled = definition.IsEnabled,
            EnabledApplications = definition.EnabledApplications,
            DisabledApplications = definition.DisabledApplications,
        };
    }
}
