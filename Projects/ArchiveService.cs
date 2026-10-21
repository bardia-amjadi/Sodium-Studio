using System.IO.Compression;
using SodiumStudio.Storage;

namespace SodiumStudio.Projects;

/// <summary>An archive problem the user can understand (invalid zip, too large, ...).</summary>
public sealed class ArchiveException : Exception
{
    public ArchiveException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>Size limits keep whole-archive handling in memory safe on a phone and bound zip bombs.</summary>
public sealed record ArchiveLimits
{
    public static ArchiveLimits Default { get; } = new();

    public int MaxEntries { get; init; } = 20_000;

    public long MaxFileBytes { get; init; } = 32L * 1024 * 1024;

    public long MaxTotalBytes { get; init; } = 64L * 1024 * 1024;

    public long MaxArchiveBytes { get; init; } = 64L * 1024 * 1024;

    public int MaxDepth { get; init; } = 64;
}

public sealed record ExportResult(byte[] Archive, int FileCount);

/// <param name="Skipped">Entries that were not imported, each with a short reason.</param>
public sealed record ImportResult(StorageNode Folder, int FileCount, IReadOnlyList<string> Skipped);

/// <summary>
/// Zip export and import for project folders. Import never executes anything, never creates
/// links, rejects absolute and parent-relative paths, and refuses archives that expand beyond
/// the limits (checked while reading, not only from the declared sizes).
/// </summary>
public sealed class ArchiveService
{
    private readonly IProjectFileSystem _fileSystem;
    private readonly ArchiveLimits _limits;

    public ArchiveService(IProjectFileSystem fileSystem, ArchiveLimits? limits = null)
    {
        _fileSystem = fileSystem;
        _limits = limits ?? ArchiveLimits.Default;
    }

    public ArchiveLimits Limits => _limits;

    // ------------------------------------------------------------------ export

    /// <summary>Zips the contents of <paramref name="folder"/> (no wrapper directory), as saved on disk.</summary>
    public async Task<ExportResult> ExportAsync(StorageNode folder, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        var counters = new Counters();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddDirectoryAsync(zip, folder, string.Empty, 0, counters, ct);
        }

        return new ExportResult(buffer.ToArray(), counters.Files);
    }

    private async Task AddDirectoryAsync(
        ZipArchive zip, StorageNode directory, string prefix, int depth, Counters counters, CancellationToken ct)
    {
        if (depth > _limits.MaxDepth)
        {
            throw new ArchiveException($"The folder is nested deeper than {_limits.MaxDepth} levels.");
        }

        var children = await _fileSystem.ListChildrenAsync(directory, ct);
        if (children.Count == 0 && prefix.Length > 0)
        {
            zip.CreateEntry(prefix); // keep empty folders
        }

        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            var path = prefix + child.Name;

            if (child.IsDirectory)
            {
                await AddDirectoryAsync(zip, child, path + "/", depth + 1, counters, ct);
                continue;
            }

            if (child.Size is { } declared && declared > _limits.MaxFileBytes)
            {
                throw new ArchiveException($"'{path}' is larger than {_limits.MaxFileBytes / (1024 * 1024)} MB and cannot be exported.");
            }

            if (++counters.Files > _limits.MaxEntries)
            {
                throw new ArchiveException($"The folder has more than {_limits.MaxEntries} files, which this build cannot export.");
            }

            var bytes = await _fileSystem.ReadAllBytesAsync(child, _limits.MaxFileBytes, ct);
            counters.Bytes += bytes.Length;
            if (counters.Bytes > _limits.MaxTotalBytes)
            {
                throw new ArchiveException($"The folder is larger than {_limits.MaxTotalBytes / (1024 * 1024)} MB, which this build cannot export.");
            }

            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = ClampTimestamp(child.LastModified);
            using var stream = entry.Open();
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    private static DateTimeOffset ClampTimestamp(DateTimeOffset? value)
    {
        // Zip stores 1980-2107 only.
        var min = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var max = new DateTimeOffset(2107, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var time = value ?? DateTimeOffset.UtcNow;
        return time < min ? min : time > max ? max : time;
    }

    // ------------------------------------------------------------------ import

    /// <summary>
    /// Extracts the archive into a NEW folder inside <paramref name="parent"/> named after the archive
    /// (made unique with " (2)", " (3)" ... if needed). On a fatal error the new folder is removed.
    /// </summary>
    public async Task<ImportResult> ImportAsync(
        StorageNode parent, string archiveFileName, byte[] archive, CancellationToken ct = default)
    {
        if (archive.LongLength > _limits.MaxArchiveBytes)
        {
            throw new ArchiveException($"The archive is larger than {_limits.MaxArchiveBytes / (1024 * 1024)} MB, which this build cannot import.");
        }

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new ArchiveException("The file is not a valid zip archive.", ex);
        }

        using (zip)
        {
            if (zip.Entries.Count > _limits.MaxEntries)
            {
                throw new ArchiveException($"The archive has more than {_limits.MaxEntries} entries, which this build cannot import.");
            }

            var folderName = await PickFreeNameAsync(parent, FolderNameFor(archiveFileName), ct);
            var root = await _fileSystem.CreateFolderAsync(parent, folderName, ct);

            try
            {
                var directories = new Dictionary<string, StorageNode>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = root };
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var skipped = new List<string>();
                long total = 0;
                var imported = 0;

                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!TryNormalize(entry.FullName, out var segments, out var isDirectory, out var reason))
                    {
                        if (reason is not null)
                        {
                            skipped.Add($"{entry.FullName}: {reason}");
                        }

                        continue;
                    }

                    try
                    {
                        if (isDirectory)
                        {
                            await EnsureDirectoryAsync(directories, segments, ct);
                            continue;
                        }

                        if (!seen.Add(string.Join('/', segments)))
                        {
                            skipped.Add($"{entry.FullName}: duplicate entry");
                            continue;
                        }

                        if (entry.Length > _limits.MaxFileBytes)
                        {
                            throw new ArchiveException($"'{entry.FullName}' is larger than {_limits.MaxFileBytes / (1024 * 1024)} MB, which this build cannot import.");
                        }

                        var data = ReadCapped(entry, Math.Min(_limits.MaxFileBytes, _limits.MaxTotalBytes - total));
                        total += data.Length;

                        var directory = await EnsureDirectoryAsync(directories, segments[..^1], ct);
                        var file = await _fileSystem.CreateFileAsync(directory, segments[^1], ct);
                        await _fileSystem.WriteAllBytesAsync(file, data, ct);
                        imported++;
                    }
                    catch (StorageException ex) when (ex.Kind is StorageErrorKind.AlreadyExists or StorageErrorKind.InvalidName)
                    {
                        // e.g. an entry "a" (file) followed by "a/b.txt" (needs "a" as a folder).
                        skipped.Add($"{entry.FullName}: {ex.Message}");
                    }
                }

                return new ImportResult(root, imported, skipped);
            }
            catch
            {
                try
                {
                    await _fileSystem.DeleteAsync(root, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Best effort.
                }

                throw;
            }
        }
    }

    private static byte[] ReadCapped(ZipArchiveEntry entry, long cap)
    {
        try
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (memory.Length + read > cap)
                {
                    throw new ArchiveException("The archive expands beyond the size limits of this build and was not imported.");
                }

                memory.Write(buffer, 0, read);
            }

            return memory.ToArray();
        }
        catch (InvalidDataException ex)
        {
            throw new ArchiveException($"'{entry.FullName}' could not be read (the archive is damaged, or the entry is encrypted).", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new ArchiveException($"'{entry.FullName}' uses an unsupported compression method.", ex);
        }
    }

    /// <summary>
    /// Splits an entry name into safe path segments. Returns false with a reason for unsafe entries
    /// (absolute, drive-letter, '.'/'..' segments, characters the app does not allow in names);
    /// a null reason means "skip silently" (macOS metadata).
    /// </summary>
    internal static bool TryNormalize(string fullName, out string[] segments, out bool isDirectory, out string? reason)
    {
        segments = Array.Empty<string>();
        reason = null;

        var name = fullName.Replace('\\', '/');
        isDirectory = name.EndsWith('/');
        name = name.TrimEnd('/');

        if (name.Length == 0)
        {
            reason = "empty name";
            return false;
        }

        if (name.StartsWith('/') || (name.Length >= 2 && name[1] == ':'))
        {
            reason = "absolute path";
            return false;
        }

        var parts = name.Split('/');
        if (parts[0] == "__MACOSX")
        {
            return false; // resource-fork metadata from macOS zip tools
        }

        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                reason = "empty path segment";
                return false;
            }

            if (part is "." or "..")
            {
                reason = "path traversal";
                return false;
            }

            if (!NameValidator.TryValidate(part, out var nameError))
            {
                reason = nameError;
                return false;
            }
        }

        segments = parts;
        return true;
    }

    private async Task<StorageNode> EnsureDirectoryAsync(
        Dictionary<string, StorageNode> directories, string[] segments, CancellationToken ct)
    {
        if (segments.Length > _limits.MaxDepth)
        {
            throw new ArchiveException($"The archive is nested deeper than {_limits.MaxDepth} levels.");
        }

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

    /// <summary>"My Project.zip" -> "My Project"; unsafe characters become '_'; never empty.</summary>
    internal static string FolderNameFor(string archiveFileName)
    {
        var name = archiveFileName.Trim();
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        var chars = name.Select(c => char.IsControl(c) || "/\\:*?\"<>|".Contains(c) ? '_' : c).ToArray();
        name = new string(chars).Trim().TrimEnd('.');
        if (name.Length > 100)
        {
            name = name[..100].TrimEnd();
        }

        return name.Length == 0 || name.All(c => c == '.' || c == '_') ? "imported" : name;
    }

    private async Task<string> PickFreeNameAsync(StorageNode parent, string baseName, CancellationToken ct)
    {
        var existing = (await _fileSystem.ListChildrenAsync(parent, ct))
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(baseName))
        {
            return baseName;
        }

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new ArchiveException($"Too many items named '{baseName}' already exist in this folder.");
    }

    private sealed class Counters
    {
        public int Files;
        public long Bytes;
    }
}
