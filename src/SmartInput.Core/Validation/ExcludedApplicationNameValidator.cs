using System.Text.RegularExpressions;

namespace SmartInput.Core.Validation;

public static partial class ExcludedApplicationNameValidator
{
    public const int MaxNameLength = 64;

    public const int MaxEntries = 32;

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidProcessNamePattern();

    public static ExcludedApplicationValidationResult ValidateText(string? text)
    {
        var entries = ParseEntries(text);
        return ValidateEntries(entries);
    }

    public static ExcludedApplicationValidationResult ValidateEntries(IReadOnlyList<string> entries)
    {
        var errors = new List<string>();
        var normalized = new List<string>();

        if (entries.Count > MaxEntries)
        {
            errors.Add($"Поддерживается не более {MaxEntries} исключённых приложений.");
        }

        foreach (var entry in entries)
        {
            if (!TryNormalizeName(entry, out var normalizedName, out var entryError))
            {
                if (entryError is not null)
                {
                    errors.Add(entryError);
                }

                continue;
            }

            if (normalized.Any(existing => string.Equals(existing, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalized.Add(normalizedName);
        }

        return new ExcludedApplicationValidationResult(normalized, errors);
    }

    public static IReadOnlyList<string> ParseEntries(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .ToList();
    }

    public static bool TryNormalizeName(string raw, out string normalizedName, out string? error)
    {
        normalizedName = raw.Trim();
        if (normalizedName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalizedName = normalizedName[..^4];
        }

        error = ValidateNormalizedName(normalizedName);
        return error is null;
    }

    public static string? ValidateSingleName(string name)
    {
        if (!TryNormalizeName(name, out var normalizedName, out var error))
        {
            return error;
        }

        return ValidateNormalizedName(normalizedName);
    }

    private static string? ValidateNormalizedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Имя приложения не может быть пустым.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"Имя приложения должно содержать не более {MaxNameLength} символов.";
        }

        if (!ValidProcessNamePattern().IsMatch(name))
        {
            return "Укажите только имя процесса: буквы, цифры, точка, дефис или подчёркивание. Пути и пробелы не допускаются.";
        }

        return null;
    }
}

public sealed class ExcludedApplicationValidationResult
{
    public ExcludedApplicationValidationResult(IReadOnlyList<string> normalizedEntries, IReadOnlyList<string> errors)
    {
        NormalizedEntries = normalizedEntries;
        Errors = errors;
    }

    public IReadOnlyList<string> NormalizedEntries { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0;
}
