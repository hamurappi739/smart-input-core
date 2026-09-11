namespace SmartInput.Core.Engines;

internal static class CapitalizationPreserver
{
    internal static string Apply(string original, string corrected)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(corrected))
        {
            return corrected;
        }

        var originalLetters = original.Where(char.IsLetter).ToArray();
        if (originalLetters.Length == 0)
        {
            return corrected;
        }

        if (originalLetters.All(char.IsUpper))
        {
            return corrected.ToUpperInvariant();
        }

        if (originalLetters.All(static character => !char.IsUpper(character)))
        {
            return corrected.ToLowerInvariant();
        }

        var firstLetterIndex = -1;
        for (var index = 0; index < original.Length; index++)
        {
            if (char.IsLetter(original[index]))
            {
                firstLetterIndex = index;
                break;
            }
        }

        if (firstLetterIndex < 0)
        {
            return corrected;
        }

        var tailLower = true;
        for (var index = firstLetterIndex + 1; index < original.Length; index++)
        {
            if (char.IsLetter(original[index]) && char.IsUpper(original[index]))
            {
                tailLower = false;
                break;
            }
        }

        if (char.IsUpper(original[firstLetterIndex]) && tailLower)
        {
            return char.ToUpperInvariant(corrected[0]) + corrected[1..].ToLowerInvariant();
        }

        if (char.IsUpper(original[firstLetterIndex]))
        {
            return char.ToUpperInvariant(corrected[0]) + corrected[1..];
        }

        return corrected;
    }
}
