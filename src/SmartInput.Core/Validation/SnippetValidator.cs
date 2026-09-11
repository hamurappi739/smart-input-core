using SmartInput.Core.Models;

namespace SmartInput.Core.Validation;

public static class SnippetValidator
{
    public const int MaxTriggerLength = 64;

    public const int MaxReplacementLength = 4096;

    public const int MaxApplicationFilters = 32;

    public static SnippetValidationResult Validate(
        SnippetDefinition definition,
        IReadOnlyList<SnippetDefinition> existing,
        Guid? excludeId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(existing);

        var errors = new List<string>();

        if (!TryNormalizeTrigger(definition.Trigger, out var trigger, out var triggerError))
        {
            errors.Add(triggerError!);
        }

        if (!TryNormalizeReplacement(definition.Replacement, out var replacement, out var replacementError))
        {
            errors.Add(replacementError!);
        }

        var enabledApps = NormalizeApplicationFilters(
            definition.EnabledApplications,
            "включённых",
            errors);
        var disabledApps = NormalizeApplicationFilters(
            definition.DisabledApplications,
            "выключенных",
            errors);

        if (errors.Count > 0)
        {
            return SnippetValidationResult.Failure(errors.ToArray());
        }

        if (existing.Any(snippet =>
                (!excludeId.HasValue || snippet.Id != excludeId.Value)
                && IsDuplicateOf(snippet, trigger, definition.Language, definition.CaseSensitive, enabledApps, disabledApps)))
        {
            return SnippetValidationResult.Failure(
                "Шаблон с таким сокращением, языком, регистром и фильтрами приложений уже существует.");
        }

        var normalized = new SnippetDefinition
        {
            Id = definition.Id == Guid.Empty ? Guid.NewGuid() : definition.Id,
            Trigger = trigger,
            Replacement = replacement,
            Language = definition.Language,
            CaseSensitive = definition.CaseSensitive,
            IsEnabled = definition.IsEnabled,
            EnabledApplications = enabledApps,
            DisabledApplications = disabledApps,
        };

        return SnippetValidationResult.Success(normalized);
    }

    public static bool TryNormalizeTrigger(string? raw, out string trigger, out string? error)
    {
        trigger = raw?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trigger))
        {
            error = "Сокращение не может быть пустым.";
            return false;
        }

        if (trigger.Length > MaxTriggerLength)
        {
            error = $"Сокращение должно содержать не более {MaxTriggerLength} символов.";
            return false;
        }

        if (trigger.Any(char.IsWhiteSpace))
        {
            error = "Сокращение не может содержать пробелы.";
            return false;
        }

        if (trigger.Any(static character => char.IsControl(character)))
        {
            error = "Сокращение не может содержать управляющие символы.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryNormalizeReplacement(string? raw, out string replacement, out string? error)
    {
        replacement = raw ?? string.Empty;
        if (replacement.Length == 0)
        {
            error = "Подстановка не может быть пустой.";
            return false;
        }

        if (replacement.Length > MaxReplacementLength)
        {
            error = $"Подстановка должна содержать не более {MaxReplacementLength} символов.";
            return false;
        }

        if (ContainsDisallowedControlCharacters(replacement))
        {
            error = "Подстановка может содержать только печатный текст, пробелы, табуляции и переводы строк.";
            return false;
        }

        error = null;
        return true;
    }

    private static IReadOnlyList<string> NormalizeApplicationFilters(
        IReadOnlyList<string>? filters,
        string filterKind,
        List<string> errors)
    {
        var source = filters ?? [];
        if (source.Count > MaxApplicationFilters)
        {
            errors.Add($"Поддерживается не более {MaxApplicationFilters} фильтров приложений ({filterKind}).");
        }

        var normalized = new List<string>();
        foreach (var entry in source)
        {
            if (!ExcludedApplicationNameValidator.TryNormalizeName(entry, out var name, out var entryError))
            {
                if (entryError is not null)
                {
                    errors.Add($"Недопустимый фильтр приложений ({filterKind}): {entryError}");
                }

                continue;
            }

            if (normalized.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalized.Add(name);
        }

        return normalized;
    }

    private static bool ContainsDisallowedControlCharacters(string value)
    {
        foreach (var character in value)
        {
            if (character is '\r' or '\n' or '\t')
            {
                continue;
            }

            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDuplicateOf(
        SnippetDefinition existing,
        string trigger,
        TypingLanguage? language,
        bool caseSensitive,
        IReadOnlyList<string> enabledApps,
        IReadOnlyList<string> disabledApps)
    {
        if (!string.Equals(existing.Trigger, trigger, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (existing.Language != language)
        {
            return false;
        }

        if (existing.CaseSensitive != caseSensitive)
        {
            return false;
        }

        return SameApplicationSet(existing.EnabledApplications, enabledApps)
            && SameApplicationSet(existing.DisabledApplications, disabledApps);
    }

    private static bool SameApplicationSet(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var leftSorted = left.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var rightSorted = right.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 0; index < leftSorted.Length; index++)
        {
            if (!string.Equals(leftSorted[index], rightSorted[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
