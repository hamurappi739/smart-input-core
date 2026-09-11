using System.Collections.Frozen;

namespace SmartInput.Core.Engines;

internal static class ProtectedTokenAnalyzer
{
    private static readonly FrozenSet<string> TechnicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Cursor",
        "GitHub",
        "GitLab",
        "VSCode",
        "VS",
        "Code",
        "Microsoft",
        "Windows",
        "Linux",
        "Docker",
        "npm",
        "Node",
        "React",
        "Angular",
        "Vue",
        "Python",
        "JavaScript",
        "TypeScript",
        "StackOverflow",
        "IntelliJ",
        "JetBrains",
        "Avalonia",
        "dotnet",
        "NuGet",
        "PowerShell",
        "SmartInput",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static bool IsProtected(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return true;
        }

        if (TechnicalNames.Contains(token))
        {
            return true;
        }

        var script = TokenScriptAnalyzer.Classify(token);
        var isInternalEnglishLayoutToken =
            script == TokenScript.Other
            && TokenScriptAnalyzer.ClassifyForLayout(token) == TokenScript.Latin;
        if (script is (TokenScript.Empty or TokenScript.Mixed or TokenScript.Other)
            && !isInternalEnglishLayoutToken)
        {
            return true;
        }

        if (ContainsDigit(token))
        {
            return true;
        }

        if (LooksLikeUrl(token))
        {
            return true;
        }

        if (LooksLikeEmail(token))
        {
            return true;
        }

        if (LooksLikeFilePath(token))
        {
            return true;
        }

        if (LooksLikeIdentifier(token))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsDigit(string token)
    {
        foreach (var character in token)
        {
            if (char.IsDigit(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeUrl(string token)
    {
        return token.Contains("://", StringComparison.Ordinal)
            || token.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeEmail(string token)
    {
        var atIndex = token.IndexOf('@');
        if (atIndex <= 0 || atIndex >= token.Length - 1)
        {
            return false;
        }

        return token[(atIndex + 1)..].Contains('.', StringComparison.Ordinal);
    }

    private static bool LooksLikeFilePath(string token)
    {
        if (token.Contains('\\', StringComparison.Ordinal))
        {
            return true;
        }

        if (token.AsSpan().Count('/') >= 2)
        {
            return true;
        }

        var lastDotIndex = token.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex >= token.Length - 1)
        {
            return false;
        }

        var extension = token[(lastDotIndex + 1)..];
        return extension.Length is >= 2 and <= 5
            && extension.All(static character => char.IsAsciiLetterOrDigit(character));
    }

    private static bool LooksLikeIdentifier(string token)
    {
        if (token.Contains('_', StringComparison.Ordinal))
        {
            return true;
        }

        if (token.Contains('-', StringComparison.Ordinal))
        {
            return true;
        }

        if (token.Length < 2)
        {
            return false;
        }

        var hasLower = false;
        var hasInternalUpper = false;

        for (var index = 0; index < token.Length; index++)
        {
            var character = token[index];
            if (!char.IsLetter(character))
            {
                continue;
            }

            if (char.IsLower(character))
            {
                hasLower = true;
                continue;
            }

            if (index > 0 && hasLower)
            {
                hasInternalUpper = true;
            }
        }

        if (hasInternalUpper)
        {
            return true;
        }

        return false;
    }
}
