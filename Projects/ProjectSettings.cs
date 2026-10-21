using System.Text.Json;
using System.Text.Json.Serialization;
using SodiumStudio.Editing;

namespace SodiumStudio.Projects;

/// <summary>
/// Per-project settings, stored inside the project folder as <see cref="FileName"/> (project data,
/// kept apart from app preferences). Arguments are a list, never a shell command line.
/// </summary>
public sealed class ProjectSettings
{
    public const string FileName = "sodium.project.json";
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Name { get; set; } = string.Empty;

    public string? TemplateId { get; set; }

    public LanguageKind Language { get; set; } = LanguageKind.PlainText;

    /// <summary>Relative path of the main file; empty means not set.</summary>
    public string EntryPoint { get; set; } = string.Empty;

    public List<string> Arguments { get; set; } = new();
}

public static class ProjectSettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(ProjectSettings settings) =>
        JsonSerializer.Serialize(settings, Options).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    public static bool TryParse(string json, out ProjectSettings? settings, out string error)
    {
        settings = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<ProjectSettings>(json, Options);
            if (parsed is null)
            {
                error = "The settings file is empty.";
                return false;
            }

            if (parsed.SchemaVersion > ProjectSettings.CurrentSchemaVersion)
            {
                error = $"The settings file was written by a newer version of Sodium Studio (schema {parsed.SchemaVersion}).";
                return false;
            }

            parsed.Name ??= string.Empty;
            parsed.EntryPoint ??= string.Empty;
            parsed.Arguments ??= new List<string>();

            if (!TryValidateEntryPoint(parsed.EntryPoint, out var entryError))
            {
                error = entryError;
                return false;
            }

            if (parsed.Arguments.Any(a => a is null))
            {
                error = "Arguments cannot contain null values.";
                return false;
            }

            settings = parsed;
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"The settings file is not valid: {ex.Message}";
            return false;
        }
    }

    /// <summary>An entry point must be empty or a relative path inside the project.</summary>
    public static bool TryValidateEntryPoint(string entryPoint, out string error)
    {
        error = string.Empty;
        if (entryPoint.Length == 0)
        {
            return true;
        }

        if (entryPoint.Length > 260)
        {
            error = "The entry point path is too long.";
            return false;
        }

        var normalized = entryPoint.Replace('\\', '/');
        if (normalized.StartsWith('/') || (normalized.Length >= 2 && normalized[1] == ':'))
        {
            error = "The entry point must be a relative path inside the project.";
            return false;
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.Any(char.IsControl))
            {
                error = "The entry point must be a relative path inside the project (no '..', empty parts or control characters).";
                return false;
            }
        }

        return true;
    }
}
