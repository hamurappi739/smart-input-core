using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

internal static class KeyboardAdjacencyMap
{
    private static readonly Dictionary<char, char[]> EnglishNeighbors = BuildLayoutNeighbors(
    [
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm",
    ]);

    private static readonly Dictionary<char, char[]> RussianNeighbors = BuildLayoutNeighbors(
    [
        "йцукенгшщзхъ",
        "фывапролджэ",
        "ячсмитьбю",
    ]);

    internal static IEnumerable<char> GetNeighbors(char character, TypingLanguage language)
    {
        var normalized = char.ToLowerInvariant(character);
        var map = language == TypingLanguage.Russian ? RussianNeighbors : EnglishNeighbors;
        return map.TryGetValue(normalized, out var neighbors)
            ? neighbors
            : [];
    }

    internal static bool AreAdjacent(char left, char right, TypingLanguage language)
    {
        var normalizedLeft = char.ToLowerInvariant(left);
        var normalizedRight = char.ToLowerInvariant(right);
        return GetNeighbors(normalizedLeft, language).Contains(normalizedRight);
    }

    internal static IEnumerable<char> GetCommonVowels(TypingLanguage language)
    {
        return language == TypingLanguage.Russian
            ? "аеиоуыэюя"
            : "aeiouy";
    }

    private static Dictionary<char, char[]> BuildLayoutNeighbors(IReadOnlyList<string> rows)
    {
        var positions = new Dictionary<char, (int Row, int Col)>();

        for (var row = 0; row < rows.Count; row++)
        {
            var line = rows[row];
            for (var col = 0; col < line.Length; col++)
            {
                positions[line[col]] = (row, col);
            }
        }

        var neighbors = new Dictionary<char, HashSet<char>>();

        foreach (var (character, (row, col)) in positions)
        {
            if (!neighbors.ContainsKey(character))
            {
                neighbors[character] = [];
            }

            AddNeighbor(neighbors, character, row, col - 1, rows);
            AddNeighbor(neighbors, character, row, col + 1, rows);
            AddNeighbor(neighbors, character, row - 1, col, rows);
            AddNeighbor(neighbors, character, row + 1, col, rows);
        }

        return neighbors.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.OrderBy(static character => character).ToArray());
    }

    private static void AddNeighbor(
        Dictionary<char, HashSet<char>> neighbors,
        char source,
        int row,
        int col,
        IReadOnlyList<string> rows)
    {
        if (row < 0 || row >= rows.Count)
        {
            return;
        }

        var line = rows[row];
        if (col < 0 || col >= line.Length)
        {
            return;
        }

        neighbors[source].Add(line[col]);
    }
}
