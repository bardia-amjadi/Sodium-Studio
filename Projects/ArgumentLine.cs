using System.Text;

namespace SodiumStudio.Projects;

/// <summary>
/// Converts between a user-editable line ("--name \"John Smith\" -v") and an argument LIST.
/// The list is what gets stored and, later, passed to a process; a command string is never executed.
/// Rules: whitespace separates arguments; double quotes group; backslash escapes a quote or backslash.
/// </summary>
public static class ArgumentLine
{
    public static bool TryParse(string line, out List<string> arguments, out string error)
    {
        arguments = new List<string>();
        error = string.Empty;

        var current = new StringBuilder();
        var hasCurrent = false;
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '\\' && i + 1 < line.Length && line[i + 1] is '"' or '\\')
            {
                current.Append(line[i + 1]);
                hasCurrent = true;
                i++;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
                hasCurrent = true; // "" yields an empty argument
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasCurrent)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    hasCurrent = false;
                }
            }
            else
            {
                current.Append(c);
                hasCurrent = true;
            }
        }

        if (inQuotes)
        {
            arguments.Clear();
            error = "A double quote is not closed.";
            return false;
        }

        if (hasCurrent)
        {
            arguments.Add(current.ToString());
        }

        return true;
    }

    public static string Format(IEnumerable<string> arguments) =>
        string.Join(' ', arguments.Select(Quote));

    private static string Quote(string argument)
    {
        var needsQuotes = argument.Length == 0 || argument.Any(c => char.IsWhiteSpace(c) || c is '"' or '\\');
        if (!needsQuotes)
        {
            return argument;
        }

        return "\"" + argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
