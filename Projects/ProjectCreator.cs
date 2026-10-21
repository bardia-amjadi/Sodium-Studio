using SodiumStudio.Storage;

namespace SodiumStudio.Projects;

/// <summary>Creates a new project folder from a template. Nothing is executed or built.</summary>
public sealed class ProjectCreator
{
    private readonly IProjectFileSystem _fileSystem;
    private readonly ProjectSettingsStore _settingsStore;

    public ProjectCreator(IProjectFileSystem fileSystem, ProjectSettingsStore settingsStore)
    {
        _fileSystem = fileSystem;
        _settingsStore = settingsStore;
    }

    /// <summary>
    /// Creates <paramref name="projectName"/> inside <paramref name="parent"/> and fills it from the template.
    /// Fails if a sibling with that name exists. If anything fails part-way, the half-created folder is removed.
    /// </summary>
    public async Task<StorageNode> CreateAsync(
        StorageNode parent, ProjectTemplate template, string projectName, CancellationToken ct = default)
    {
        projectName = projectName.Trim();
        if (!NameValidator.TryValidate(projectName, out var error))
        {
            throw new StorageException(StorageErrorKind.InvalidName, error);
        }

        var folder = await _fileSystem.CreateFolderAsync(parent, projectName, ct);
        try
        {
            var directories = new Dictionary<string, StorageNode> { [string.Empty] = folder };

            foreach (var templateFile in template.Files)
            {
                ct.ThrowIfCancellationRequested();

                var path = ProjectTemplates.Render(templateFile.Path, projectName);
                var segments = path.Split('/');
                var directory = await EnsureDirectoryAsync(directories, segments[..^1], ct);

                var file = await _fileSystem.CreateFileAsync(directory, segments[^1], ct);
                var content = ProjectTemplates.Render(templateFile.Content, projectName);
                await _fileSystem.WriteAllBytesAsync(file, TextFileCodec.Encode(content, LineEnding.Lf, withBom: false), ct);
            }

            var settings = new ProjectSettings
            {
                Name = projectName,
                TemplateId = template.Id,
                Language = template.Language,
                EntryPoint = ProjectTemplates.Render(template.EntryPoint, projectName),
            };
            await _settingsStore.SaveAsync(folder, null, settings, ct);
        }
        catch
        {
            try
            {
                await _fileSystem.DeleteAsync(folder, CancellationToken.None);
            }
            catch (Exception)
            {
                // Best effort: report the original failure, not the cleanup failure.
            }

            throw;
        }

        return folder;
    }

    private async Task<StorageNode> EnsureDirectoryAsync(
        Dictionary<string, StorageNode> directories, string[] segments, CancellationToken ct)
    {
        var key = string.Empty;
        var current = directories[key];
        foreach (var segment in segments)
        {
            key = key.Length == 0 ? segment : key + "/" + segment;
            if (!directories.TryGetValue(key, out var next))
            {
                next = await _fileSystem.CreateFolderAsync(current, segment, ct);
                directories[key] = next;
            }

            current = next;
        }

        return current;
    }
}
