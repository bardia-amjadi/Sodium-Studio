using SodiumStudio.Storage;

namespace SodiumStudio.Projects;

/// <param name="File">The settings file, or null if the folder has none.</param>
/// <param name="Settings">Parsed settings, or null if absent or invalid.</param>
/// <param name="Error">Why the file could not be used (present but unreadable or invalid), else null.</param>
public sealed record LoadedSettings(StorageNode? File, ProjectSettings? Settings, string? Error);

/// <summary>Reads and writes <see cref="ProjectSettings.FileName"/> inside a project folder.</summary>
public sealed class ProjectSettingsStore
{
    private const long MaxSettingsBytes = 256 * 1024;

    private readonly IProjectFileSystem _fileSystem;

    public ProjectSettingsStore(IProjectFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<LoadedSettings> LoadAsync(StorageNode folder, CancellationToken ct = default)
    {
        var children = await _fileSystem.ListChildrenAsync(folder, ct);
        var file = children.FirstOrDefault(c => !c.IsDirectory && c.Name == ProjectSettings.FileName);
        if (file is null)
        {
            return new LoadedSettings(null, null, null);
        }

        byte[] bytes;
        try
        {
            bytes = await _fileSystem.ReadAllBytesAsync(file, MaxSettingsBytes, ct);
        }
        catch (StorageException ex)
        {
            return new LoadedSettings(file, null, ex.Message);
        }

        if (!TextFileCodec.TryDecode(bytes, out var decoded, out var decodeError))
        {
            return new LoadedSettings(file, null, decodeError);
        }

        return ProjectSettingsSerializer.TryParse(decoded!.Text, out var settings, out var parseError)
            ? new LoadedSettings(file, settings, null)
            : new LoadedSettings(file, null, parseError);
    }

    /// <summary>Writes the settings, overwriting <paramref name="existingFile"/> or creating the file.</summary>
    public async Task<StorageNode> SaveAsync(
        StorageNode folder, StorageNode? existingFile, ProjectSettings settings, CancellationToken ct = default)
    {
        if (!ProjectSettingsSerializer.TryValidateEntryPoint(settings.EntryPoint, out var error))
        {
            throw new StorageException(StorageErrorKind.InvalidName, error);
        }

        var bytes = TextFileCodec.Encode(ProjectSettingsSerializer.Serialize(settings), LineEnding.Lf, withBom: false);

        var file = existingFile ?? await _fileSystem.CreateFileAsync(folder, ProjectSettings.FileName, ct);
        await _fileSystem.WriteAllBytesAsync(file, bytes, ct);
        return file;
    }
}
