namespace SodiumStudio.Editing;

public enum TokenKind
{
    Keyword,
    String,
    Comment,
    Number,
    Preprocessor,
}

/// <summary>A highlighted span: UTF-16 offset and length into the source text.</summary>
public readonly record struct Token(int Start, int Length, TokenKind Kind);

/// <summary>
/// A small lexical highlighter (keywords, strings, comments, numbers, preprocessor lines).
/// This is syntax COLORING only: it does not parse, resolve types or report errors.
/// Known approximations: JavaScript regex literals, nested template-literal expressions,
/// C++ raw strings and C# interpolation holes are not modelled.
/// Tokens are returned in order, non-empty and non-overlapping.
/// </summary>
public static class SyntaxHighlighter
{
    private sealed record Profile(
        HashSet<string> Keywords,
        string[] LineComments,
        string? BlockStart,
        string? BlockEnd,
        char[] Quotes,
        bool TripleQuotes,
        bool VerbatimAtStrings,
        bool BacktickStrings,
        bool HashPreprocessor);

    private static readonly Profile CSharp = new(
        Words("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while var async await record init required with yield get set partial when where nameof dynamic global file scoped"),
        new[] { "//" }, "/*", "*/", new[] { '"', '\'' },
        TripleQuotes: true, VerbatimAtStrings: true, BacktickStrings: false, HashPreprocessor: true);

    private static readonly Profile Python = new(
        Words("False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case self"),
        new[] { "#" }, null, null, new[] { '"', '\'' },
        TripleQuotes: true, VerbatimAtStrings: false, BacktickStrings: false, HashPreprocessor: false);

    private static readonly Profile Cpp = new(
        Words("alignas alignof and asm auto bool break case catch char class concept const consteval constexpr constinit const_cast continue co_await co_return co_yield decltype default delete do double dynamic_cast else enum explicit export extern false float for friend goto if inline int long mutable namespace new noexcept not nullptr operator or private protected public register reinterpret_cast requires return short signed sizeof static static_assert static_cast struct switch template this thread_local throw true try typedef typeid typename union unsigned using virtual void volatile wchar_t while xor override final NULL"),
        new[] { "//" }, "/*", "*/", new[] { '"', '\'' },
        TripleQuotes: false, VerbatimAtStrings: false, BacktickStrings: false, HashPreprocessor: true);

    private static readonly Profile Java = new(
        Words("abstract assert boolean break byte case catch char class const continue default do double else enum extends final finally float for goto if implements import instanceof int interface long native new null package private protected public return short static strictfp super switch synchronized this throw throws transient true false try void volatile while var record sealed permits yield non-sealed"),
        new[] { "//" }, "/*", "*/", new[] { '"', '\'' },
        TripleQuotes: true, VerbatimAtStrings: false, BacktickStrings: false, HashPreprocessor: false);

    private static readonly Profile JavaScript = new(
        Words("async await break case catch class const continue debugger default delete do else export extends false finally for from function if import in instanceof let new null of return static super switch this throw true try typeof undefined var void while with yield get set"),
        new[] { "//" }, "/*", "*/", new[] { '"', '\'' },
        TripleQuotes: false, VerbatimAtStrings: false, BacktickStrings: true, HashPreprocessor: false);

    public static IReadOnlyList<Token> Tokenize(string text, LanguageKind language)
    {
        var tokens = new List<Token>();
        var profile = language switch
        {
            LanguageKind.CSharp => CSharp,
            LanguageKind.Python => Python,
            LanguageKind.Cpp => Cpp,
            LanguageKind.Java => Java,
            LanguageKind.JavaScript => JavaScript,
            _ => null,
        };

        if (profile is null || text.Length == 0)
        {
            return tokens;
        }

        var i = 0;
        var atLineStart = true; // only whitespace seen so far on this line
        var n = text.Length;

        while (i < n)
        {
            var c = text[i];

            if (c == '\n')
            {
                atLineStart = true;
                i++;
                continue;
            }

            if (c == ' ' || c == '\t' || c == '\r')
            {
                i++;
                continue;
            }

            var wasLineStart = atLineStart;
            atLineStart = false;

            // Preprocessor line (#include, #region, ...), not a comment.
            if (profile.HashPreprocessor && c == '#' && wasLineStart)
            {
                var end = LineEnd(text, i);
                tokens.Add(new Token(i, end - i, TokenKind.Preprocessor));
                i = end;
                continue;
            }

            // Line comments.
            if (StartsWithAny(text, i, profile.LineComments))
            {
                var end = LineEnd(text, i);
                tokens.Add(new Token(i, end - i, TokenKind.Comment));
                i = end;
                continue;
            }

            // Block comments (unterminated runs to end of file).
            if (profile.BlockStart is not null && string.CompareOrdinal(text, i, profile.BlockStart, 0, profile.BlockStart.Length) == 0)
            {
                var close = text.IndexOf(profile.BlockEnd!, i + profile.BlockStart.Length, StringComparison.Ordinal);
                var end = close < 0 ? n : close + profile.BlockEnd!.Length;
                tokens.Add(new Token(i, end - i, TokenKind.Comment));
                i = end;
                continue;
            }

            // C# verbatim string: @"..." ("" is an escaped quote), may span lines.
            if (profile.VerbatimAtStrings && c == '@' && i + 1 < n && text[i + 1] == '"')
            {
                var end = ScanVerbatim(text, i + 2);
                tokens.Add(new Token(i, end - i, TokenKind.String));
                i = end;
                continue;
            }

            // Strings.
            if (Array.IndexOf(profile.Quotes, c) >= 0 || (profile.BacktickStrings && c == '`'))
            {
                int end;
                if (c == '`')
                {
                    end = ScanQuoted(text, i + 1, '`', allowNewline: true);
                }
                else if (profile.TripleQuotes && i + 2 < n && text[i + 1] == c && text[i + 2] == c)
                {
                    var close = text.IndexOf(new string(c, 3), i + 3, StringComparison.Ordinal);
                    end = close < 0 ? n : close + 3;
                }
                else
                {
                    end = ScanQuoted(text, i + 1, c, allowNewline: false);
                }

                tokens.Add(new Token(i, end - i, TokenKind.String));
                i = end;
                continue;
            }

            // Numbers (not when glued to an identifier).
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < n && char.IsAsciiDigit(text[i + 1])))
            {
                var end = i + 1;
                while (end < n && (char.IsAsciiLetterOrDigit(text[end]) || text[end] == '_' || text[end] == '.'))
                {
                    // Stop at a member access such as "1.ToString" is not a concern; "1..2" ranges are rare.
                    if (text[end] == '.' && (end + 1 >= n || !char.IsAsciiDigit(text[end + 1])))
                    {
                        break;
                    }

                    end++;
                }

                tokens.Add(new Token(i, end - i, TokenKind.Number));
                i = end;
                continue;
            }

            // Identifiers / keywords.
            if (IsIdentStart(c))
            {
                var end = i + 1;
                while (end < n && IsIdentPart(text[end]))
                {
                    end++;
                }

                if (profile.Keywords.Contains(text.Substring(i, end - i)))
                {
                    tokens.Add(new Token(i, end - i, TokenKind.Keyword));
                }

                i = end;
                continue;
            }

            i++;
        }

        return tokens;
    }

    private static HashSet<string> Words(string space) =>
        new(space.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '$';

    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static int LineEnd(string text, int from)
    {
        var end = text.IndexOf('\n', from);
        if (end < 0)
        {
            return text.Length;
        }

        // Keep the CR of a CRLF out of the token.
        return end > from && text[end - 1] == '\r' ? end - 1 : end;
    }

    private static bool StartsWithAny(string text, int index, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (string.CompareOrdinal(text, index, prefix, 0, prefix.Length) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the index just after the closing quote (or the line end if unterminated).</summary>
    private static int ScanQuoted(string text, int from, char quote, bool allowNewline)
    {
        var i = from;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (c == quote)
            {
                return i + 1;
            }

            if (c == '\n' && !allowNewline)
            {
                return i > from && text[i - 1] == '\r' ? i - 1 : i;
            }

            i++;
        }

        return text.Length;
    }

    private static int ScanVerbatim(string text, int from)
    {
        var i = from;
        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return text.Length;
    }
}
