using System.Text;

namespace SmartInput.Core.Services;

/// <summary>
/// Splits selected text into preserved separators and letter-cores while keeping
/// whitespace, punctuation, and multiline structure intact.
/// </summary>
internal static class SelectedTextSegmenter
{
    internal readonly record struct Segment(string Text, bool IsCorrectableWord);

    internal static IReadOnlyList<Segment> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return [];
        }

        var segments = new List<Segment>();
        var index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                var start = index;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                segments.Add(new Segment(text[start..index], IsCorrectableWord: false));
                continue;
            }

            var chunkStart = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            AppendChunk(segments, text[chunkStart..index]);
        }

        return segments;
    }

    internal static string Join(IEnumerable<Segment> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            builder.Append(segment.Text);
        }

        return builder.ToString();
    }

    private static void AppendChunk(List<Segment> segments, string chunk)
    {
        if (chunk.Length == 0)
        {
            return;
        }

        var leadingEnd = 0;
        while (leadingEnd < chunk.Length && !char.IsLetter(chunk[leadingEnd]))
        {
            leadingEnd++;
        }

        var trailingStart = chunk.Length;
        while (trailingStart > leadingEnd && !char.IsLetter(chunk[trailingStart - 1]))
        {
            trailingStart--;
        }

        if (leadingEnd >= trailingStart)
        {
            segments.Add(new Segment(chunk, IsCorrectableWord: false));
            return;
        }

        if (leadingEnd > 0)
        {
            segments.Add(new Segment(chunk[..leadingEnd], IsCorrectableWord: false));
        }

        var core = chunk[leadingEnd..trailingStart];
        var coreIsLettersOnly = core.All(char.IsLetter);
        segments.Add(new Segment(core, IsCorrectableWord: coreIsLettersOnly));

        if (trailingStart < chunk.Length)
        {
            segments.Add(new Segment(chunk[trailingStart..], IsCorrectableWord: false));
        }
    }
}
