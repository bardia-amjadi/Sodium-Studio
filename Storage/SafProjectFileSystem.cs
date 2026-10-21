using Android.Content;
using Android.Database;
using Android.Provider;
using Android.Webkit;
using AndroidUri = Android.Net.Uri;

namespace SodiumStudio.Storage;

/// <summary>
/// Storage Access Framework implementation. Everything goes through
/// <see cref="DocumentsContract"/> and <see cref="ContentResolver"/>; no java.io.File
/// or filesystem paths are used, because content URIs are not paths.
/// All blocking provider calls run on the thread pool.
/// </summary>
public sealed class SafProjectFileSystem : IProjectFileSystem
{
    private const string RootUriPreferenceKey = "storage.rootTreeUri";

    private const ActivityFlags ReadWrite =
        ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission;

    // Column names and values from android.provider.DocumentsContract.Document. Literal strings
    // are used (they are a stable part of the Android API) to avoid depending on binding names.
    private const string ColId = "document_id";
    private const string ColName = "_display_name";
    private const string ColMime = "mime_type";
    private const string ColSize = "_size";
    private const string ColModified = "last_modified";
    private const string ColFlags = "flags";
    private const string MimeDirectory = "vnd.android.document/directory";
    private const string MimeUnknown = "application/octet-stream";

    // DocumentsContract.Document.FLAG_* bit values.
    private const int FlagSupportsWrite = 2;        // FLAG_SUPPORTS_WRITE
    private const int FlagSupportsDelete = 4;       // FLAG_SUPPORTS_DELETE
    private const int FlagDirSupportsCreate = 8;    // FLAG_DIR_SUPPORTS_CREATE
    private const int FlagSupportsRename = 64;      // FLAG_SUPPORTS_RENAME

    private static readonly string[] NodeProjection =
        { ColId, ColName, ColMime, ColSize, ColModified, ColFlags };

    private static ContentResolver Resolver =>
        Microsoft.Maui.ApplicationModel.Platform.AppContext.ContentResolver
        ?? throw new StorageException(StorageErrorKind.Failed, "The Android content resolver is unavailable.");

    // ---------------------------------------------------------------- root folder

    public async Task<StorageNode?> PickRootFolderAsync(CancellationToken ct = default)
    {
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not MainActivity activity)
        {
            throw new StorageException(StorageErrorKind.Failed,
                "No active window is available to show the folder picker.");
        }

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ReadWrite
                        | ActivityFlags.GrantPersistableUriPermission
                        | ActivityFlags.GrantPrefixUriPermission);

        var result = await activity.PickFolderAsync(intent).WaitAsync(ct);
        var treeUri = result?.Data;
        if (treeUri is null)
        {
            return null; // user cancelled
        }

        return await RunAsync(() => AdoptTree(treeUri), ct);
    }

    public Task<StorageNode?> RestoreRootFolderAsync(CancellationToken ct = default) =>
        RunAsync<StorageNode?>(() =>
        {
            var saved = Microsoft.Maui.Storage.Preferences.Default.Get(RootUriPreferenceKey, string.Empty);
            if (saved.Length == 0)
            {
                return null;
            }

            var stillGranted = Resolver.PersistedUriPermissions?.Any(p =>
                p.Uri?.ToString() == saved && p.IsReadPermission && p.IsWritePermission) ?? false;
            if (!stillGranted)
            {
                Microsoft.Maui.Storage.Preferences.Default.Remove(RootUriPreferenceKey);
                throw new StorageException(StorageErrorKind.PermissionDenied,
                    "Access to the previously opened folder is no longer granted. Please choose the folder again.");
            }

            try
            {
                return LoadRoot(AndroidUri.Parse(saved)!);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var translated = Translate(ex);
                if (translated.Kind is StorageErrorKind.NotFound or StorageErrorKind.PermissionDenied)
                {
                    Microsoft.Maui.Storage.Preferences.Default.Remove(RootUriPreferenceKey);
                }

                throw translated;
            }
        }, ct);

    public Task CloseRootFolderAsync(StorageNode root, CancellationToken ct = default) =>
        RunVoidAsync(() =>
        {
            ReleaseQuietly(root.TreeUri);
            Microsoft.Maui.Storage.Preferences.Default.Remove(RootUriPreferenceKey);
        }, ct);

    private StorageNode AdoptTree(AndroidUri treeUri)
    {
        var previous = Microsoft.Maui.Storage.Preferences.Default.Get(RootUriPreferenceKey, string.Empty);
        var newUriText = treeUri.ToString()!;

        Resolver.TakePersistableUriPermission(treeUri, ReadWrite);

        StorageNode root;
        try
        {
            root = LoadRoot(treeUri);
            if (!root.IsDirectory)
            {
                throw new StorageException(StorageErrorKind.Failed, "The selected item is not a folder.");
            }
        }
        catch
        {
            if (previous != newUriText)
            {
                ReleaseQuietly(newUriText);
            }

            throw;
        }

        // Android caps the number of persisted grants per app, so drop the old one.
        if (previous.Length > 0 && previous != newUriText)
        {
            ReleaseQuietly(previous);
        }

        Microsoft.Maui.Storage.Preferences.Default.Set(RootUriPreferenceKey, newUriText);
        return root;
    }

    private static StorageNode LoadRoot(AndroidUri treeUri) =>
        QuerySingle(treeUri.ToString()!, DocumentsContract.GetTreeDocumentId(treeUri)!);

    private static void ReleaseQuietly(string uriText)
    {
        try
        {
            Resolver.ReleasePersistableUriPermission(AndroidUri.Parse(uriText)!, ReadWrite);
        }
        catch (Exception)
        {
            // Releasing is best effort; the grant may already be gone.
        }
    }

    // ---------------------------------------------------------------- browsing

    public Task<IReadOnlyList<StorageNode>> ListChildrenAsync(StorageNode directory, CancellationToken ct = default) =>
        RunAsync(() => ListChildren(directory), ct);

    private static IReadOnlyList<StorageNode> ListChildren(StorageNode directory)
    {
        if (!directory.IsDirectory)
        {
            throw new StorageException(StorageErrorKind.NotSupported, $"'{directory.Name}' is not a folder.");
        }

        var treeUri = AndroidUri.Parse(directory.TreeUri)!;
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, directory.DocumentId)!;

        using var cursor = Resolver.Query(childrenUri, NodeProjection, null, null, null)
            ?? throw new StorageException(StorageErrorKind.Failed,
                "The storage provider did not return a listing for this folder.");

        var result = new List<StorageNode>(cursor.Count);
        while (cursor.MoveToNext())
        {
            result.Add(ReadNode(cursor, directory.TreeUri));
        }

        result.Sort(CompareNodes);
        return result;
    }

    private static int CompareNodes(StorageNode a, StorageNode b)
    {
        if (a.IsDirectory != b.IsDirectory)
        {
            return a.IsDirectory ? -1 : 1;
        }

        return StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
    }

    // ---------------------------------------------------------------- mutations

    public Task<StorageNode> CreateFileAsync(StorageNode parent, string name, CancellationToken ct = default) =>
        RunAsync(() => Create(parent, name, MimeTypeForFileName(name)), ct);

    public Task<StorageNode> CreateFolderAsync(StorageNode parent, string name, CancellationToken ct = default) =>
        RunAsync(() => Create(parent, name, MimeDirectory), ct);

    public Task<StorageNode> RenameAsync(StorageNode parent, StorageNode node, string newName, CancellationToken ct = default) =>
        RunAsync(() =>
        {
            EnsureValidName(newName);
            if (!node.CanRename)
            {
                throw new StorageException(StorageErrorKind.NotSupported,
                    $"'{node.Name}' cannot be renamed in this location.");
            }

            if (newName == node.Name)
            {
                return node;
            }

            EnsureNameAvailable(parent, newName, node.DocumentId);

            var docUri = DocumentUri(node.TreeUri, node.DocumentId);
            var renamed = DocumentsContract.RenameDocument(Resolver, docUri, newName)
                ?? throw new StorageException(StorageErrorKind.Failed,
                    "The storage provider could not rename this item.");

            return QuerySingle(node.TreeUri, DocumentsContract.GetDocumentId(renamed)!);
        }, ct);

    public Task DeleteAsync(StorageNode node, CancellationToken ct = default) =>
        RunVoidAsync(() =>
        {
            if (!node.CanDelete)
            {
                throw new StorageException(StorageErrorKind.NotSupported,
                    $"'{node.Name}' cannot be deleted from this location.");
            }

            var docUri = DocumentUri(node.TreeUri, node.DocumentId);
            if (!DocumentsContract.DeleteDocument(Resolver, docUri))
            {
                throw new StorageException(StorageErrorKind.Failed,
                    $"The storage provider could not delete '{node.Name}'.");
            }
        }, ct);

    // ---------------------------------------------------------------- file content

    public Task<byte[]> ReadAllBytesAsync(StorageNode file, long maxBytes, CancellationToken ct = default) =>
        RunAsync(() =>
        {
            if (file.IsDirectory)
            {
                throw new StorageException(StorageErrorKind.NotSupported, $"'{file.Name}' is a folder.");
            }

            using var input = Resolver.OpenInputStream(DocumentUri(file.TreeUri, file.DocumentId))
                ?? throw new StorageException(StorageErrorKind.Failed,
                    $"The storage provider could not open '{file.Name}' for reading.");

            using var buffer = new MemoryStream();
            var chunk = new byte[64 * 1024];
            int read;
            while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (buffer.Length + read > maxBytes)
                {
                    throw new StorageException(StorageErrorKind.NotSupported,
                        $"'{file.Name}' is larger than the {maxBytes / (1024 * 1024)} MB this build can open.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }, ct);

    public Task WriteAllBytesAsync(StorageNode file, byte[] content, CancellationToken ct = default) =>
        RunVoidAsync(() =>
        {
            if (file.IsDirectory)
            {
                throw new StorageException(StorageErrorKind.NotSupported, $"'{file.Name}' is a folder.");
            }

            if (!file.CanWrite)
            {
                throw new StorageException(StorageErrorKind.PermissionDenied,
                    $"'{file.Name}' is read-only in this location.");
            }

            var docUri = DocumentUri(file.TreeUri, file.DocumentId);

            // "wt" = write + truncate. Without truncation a shorter file would keep stale tail bytes.
            using (var output = Resolver.OpenOutputStream(docUri, "wt")
                ?? throw new StorageException(StorageErrorKind.Failed,
                    $"The storage provider could not open '{file.Name}' for writing."))
            {
                output.Write(content, 0, content.Length);
                output.Flush();
            }

            // Read-back check of the reported size, where the provider reports one.
            var after = QuerySingle(file.TreeUri, file.DocumentId);
            if (after.Size is { } size && size != content.Length)
            {
                throw new StorageException(StorageErrorKind.Failed,
                    $"Saving '{file.Name}' may have failed: expected {content.Length} bytes but the provider reports {size}.");
            }
        }, ct);

    public Task<bool> ExistsAsync(StorageNode node, CancellationToken ct = default) =>
        RunAsync(() =>
        {
            try
            {
                QuerySingle(node.TreeUri, node.DocumentId);
                return true;
            }
            catch (StorageException ex) when (ex.Kind == StorageErrorKind.NotFound)
            {
                return false;
            }
            catch (Java.Lang.IllegalArgumentException)
            {
                return false; // some providers reject ids of documents that no longer exist
            }
            catch (Java.IO.FileNotFoundException)
            {
                return false;
            }
        }, ct);

    private static StorageNode Create(StorageNode parent, string name, string mimeType)
    {
        EnsureValidName(name);
        if (!parent.CanCreateChildren)
        {
            throw new StorageException(StorageErrorKind.NotSupported,
                $"New items cannot be created inside '{parent.Name}'.");
        }

        // Providers silently rename on collision ("name (1)"), so check first.
        EnsureNameAvailable(parent, name, exceptDocumentId: null);

        var parentUri = DocumentUri(parent.TreeUri, parent.DocumentId);
        var created = DocumentsContract.CreateDocument(Resolver, parentUri, mimeType, name)
            ?? throw new StorageException(StorageErrorKind.Failed,
                "The storage provider could not create the item.");

        return QuerySingle(parent.TreeUri, DocumentsContract.GetDocumentId(created)!);
    }

    /// <summary>
    /// The external-storage provider appends an extension when the MIME type does not match the
    /// one implied by the file name. Deriving the MIME type from the name with the same
    /// MimeTypeMap keeps the name unchanged; unknown extensions use octet-stream, which the
    /// provider also leaves alone.
    /// </summary>
    private static string MimeTypeForFileName(string name)
    {
        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        if (extension.Length == 0)
        {
            return MimeUnknown;
        }

        return MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? MimeUnknown;
    }

    private static void EnsureValidName(string name)
    {
        if (!NameValidator.TryValidate(name, out var error))
        {
            throw new StorageException(StorageErrorKind.InvalidName, error);
        }
    }

    private static void EnsureNameAvailable(StorageNode parent, string name, string? exceptDocumentId)
    {
        foreach (var sibling in ListChildren(parent))
        {
            if (sibling.DocumentId != exceptDocumentId &&
                string.Equals(sibling.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                throw new StorageException(StorageErrorKind.AlreadyExists,
                    $"An item named '{sibling.Name}' already exists in this folder.");
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static AndroidUri DocumentUri(string treeUri, string documentId) =>
        DocumentsContract.BuildDocumentUriUsingTree(AndroidUri.Parse(treeUri)!, documentId)!;

    private static StorageNode QuerySingle(string treeUri, string documentId)
    {
        using var cursor = Resolver.Query(DocumentUri(treeUri, documentId), NodeProjection, null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            throw new StorageException(StorageErrorKind.NotFound, "The item could not be found.");
        }

        return ReadNode(cursor, treeUri);
    }

    private static StorageNode ReadNode(ICursor cursor, string treeUri)
    {
        var id = GetString(cursor, ColId)
            ?? throw new StorageException(StorageErrorKind.Failed, "The storage provider returned an item without an id.");
        var name = GetString(cursor, ColName) ?? id;
        var isDirectory = GetString(cursor, ColMime) == MimeDirectory;
        long? size = isDirectory ? null : GetLong(cursor, ColSize);
        var modified = GetLong(cursor, ColModified);
        var flags = (int)(GetLong(cursor, ColFlags) ?? 0);

        DateTimeOffset? lastModified = modified is > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(modified.Value)
            : null;

        return new StorageNode(
            TreeUri: treeUri,
            DocumentId: id,
            Name: name,
            IsDirectory: isDirectory,
            Size: size,
            LastModified: lastModified,
            CanCreateChildren: isDirectory && (flags & FlagDirSupportsCreate) != 0,
            CanRename: (flags & FlagSupportsRename) != 0,
            CanDelete: (flags & FlagSupportsDelete) != 0,
            CanWrite: (flags & FlagSupportsWrite) != 0);
    }

    private static string? GetString(ICursor cursor, string column)
    {
        var index = cursor.GetColumnIndex(column);
        return index < 0 || cursor.IsNull(index) ? null : cursor.GetString(index);
    }

    private static long? GetLong(ICursor cursor, string column)
    {
        var index = cursor.GetColumnIndex(column);
        return index < 0 || cursor.IsNull(index) ? null : cursor.GetLong(index);
    }

    private static StorageException Translate(Exception ex) => ex switch
    {
        StorageException s => s,
        Java.Lang.SecurityException => new StorageException(StorageErrorKind.PermissionDenied,
            "Permission was denied by the storage provider. Access to this folder may have been revoked.", ex),
        Java.IO.FileNotFoundException => new StorageException(StorageErrorKind.NotFound,
            "The item no longer exists.", ex),
        Java.Lang.UnsupportedOperationException => new StorageException(StorageErrorKind.NotSupported,
            "This storage location does not support that operation.", ex),
        _ => new StorageException(StorageErrorKind.Failed, ex.Message, ex),
    };

    private static Task<T> RunAsync<T>(Func<T> work, CancellationToken ct) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return work();
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not StorageException)
            {
                throw Translate(ex);
            }
        }, ct);

    private static Task RunVoidAsync(Action work, CancellationToken ct) =>
        RunAsync(() =>
        {
            work();
            return true;
        }, ct);
}
