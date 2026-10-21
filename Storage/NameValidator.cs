namespace SodiumStudio.Storage;

/// <summary>
/// Validates file and folder names before they reach a storage provider.
/// Several providers (notably the external-storage provider) silently rewrite
/// names containing reserved characters, so we reject them up front instead of
/// letting an item appear under a different name than the user typed.
/// </summary>
public static class NameValidator
{
    private static readonly char[] Forbidden = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    public static bool TryValidate(string? name, out string error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "The name cannot be empty.";
            return false;
        }

        if (name is "." or "..")
        {
            error = "'.' and '..' are not valid names.";
            return false;
        }

        if (name.Length > 255)
        {
            error = "The name is too long (255 characters maximum).";
            return false;
        }

        foreach (var c in name)
        {
            if (char.IsControl(c))
            {
                error = "The name cannot contain control characters.";
                return false;
            }
        }

        var index = name.IndexOfAny(Forbidden);
        if (index >= 0)
        {
            error = $"The name cannot contain '{name[index]}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
