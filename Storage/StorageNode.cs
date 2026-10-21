namespace SodiumStudio.Storage;

/// <summary>
/// A file or folder inside a user-selected Storage Access Framework tree.
/// This is NOT a filesystem path. <see cref="TreeUri"/> + <see cref="DocumentId"/>
/// identify the document; both are opaque strings owned by the storage provider.
/// </summary>
public sealed record StorageNode(
    string TreeUri,
    string DocumentId,
    string Name,
    bool IsDirectory,
    long? Size,
    DateTimeOffset? LastModified,
    bool CanCreateChildren,
    bool CanRename,
    bool CanDelete,
    bool CanWrite);
