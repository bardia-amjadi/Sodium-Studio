namespace SodiumStudio.Editing;

/// <summary>Plain-text (non-regex) search helpers used by the find/replace bar.</summary>
public static class TextSearch
{
    /// <summary>
    /// First match starting at or after <paramref name="from"/>, wrapping to the start of the
    /// text. Returns -1 when there is no match.
    /// </summary>
    public static int FindNext(string text, string term, int from, bool ignoreCase)
    {
        if (term.Length == 0 || text.Length == 0)
        {
            return -1;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        from = Math.Clamp(from, 0, text.Length);

        var index = text.IndexOf(term, from, comparison);
        return index >= 0 ? index : text.IndexOf(term, 0, comparison);
    }

    /// <summary>
    /// Last match that starts strictly before <paramref name="before"/>, wrapping to the end of
    /// the text. Returns -1 when there is no match.
    /// </summary>
    public static int FindPrevious(string text, string term, int before, bool ignoreCase)
    {
        if (term.Length == 0 || text.Length == 0)
        {
            return -1;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        before = Math.Clamp(before, 0, text.Length);

        if (before > 0)
        {
            // LastIndexOf's startIndex is the last character the match may cover, so a match
            // starting at before-1 may end at before-1 + term.Length-1.
            var startIndex = Math.Min(text.Length - 1, before - 1 + term.Length - 1);
            var index = text.LastIndexOf(term, startIndex, comparison);
            if (index >= 0)
            {
                return index;
            }
        }

        return text.LastIndexOf(term, text.Length - 1, comparison);
    }

    /// <summary>Start offsets of all non-overlapping matches, in ascending order.</summary>
    public static List<int> FindAll(string text, string term, bool ignoreCase)
    {
        var result = new List<int>();
        if (term.Length == 0)
        {
            return result;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var position = 0;
        while (position <= text.Length - term.Length)
        {
            var index = text.IndexOf(term, position, comparison);
            if (index < 0)
            {
                break;
            }

            result.Add(index);
            position = index + term.Length;
        }

        return result;
    }
}
