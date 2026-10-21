namespace SodiumStudio.Storage;

/// <summary>
/// Access to the user-selected project folder. The interface keeps Android types out of the
/// view models; the single implementation is <see cref="SafProjectFileSystem"/>.
/// All members throw <see cref="StorageException"/> on storage failures.
/// </summary>
public interface IProjectFileSystem
{
    /// <summary>Shows the system folder picker. Returns null if the user cancelled.</summary>
    Task<StorageNode?> PickRootFolderAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-opens the folder chosen in a previous session, if access is still granted.
    /// Returns null if no folder was remembered; throws if access was lost.
    /// </summary>
    Task<StorageNode?> RestoreRootFolderAsync(CancellationToken ct = default);

    /// <summary>Forgets the folder and releases the persisted access grant.</summary>
    Task CloseRootFolderAsync(StorageNode root, CancellationToken ct = default);

    /// <summary>Lists direct children, folders first, then alphabetical.</summary>
    Task<IReadOnlyList<StorageNode>> ListChildrenAsync(StorageNode directory, CancellationToken ct = default);

    Task<StorageNode> CreateFileAsync(StorageNode parent, string name, CancellationToken ct = default);

    Task<StorageNode> CreateFolderAsync(StorageNode parent, string name, CancellationToken ct = default);

    /// <summary>Renames an item. The returned node may have a different DocumentId.</summary>
    Task<StorageNode> RenameAsync(StorageNode parent, StorageNode node, string newName, CancellationToken ct = default);

    /// <summary>Deletes a file, or a folder with everything inside it.</summary>
    Task DeleteAsync(StorageNode node, CancellationToken ct = default);

    /// <summary>Reads a whole file. Throws if it is larger than <paramref name="maxBytes"/>.</summary>
    Task<byte[]> ReadAllBytesAsync(StorageNode file, long maxBytes, CancellationToken ct = default);

    /// <summary>
    /// Replaces the content of an existing file (truncate and write). Not atomic: if the provider
    /// fails part-way the file may be left incomplete, which is why callers keep the editor
    /// buffer dirty when this throws. The written size is checked afterwards where the provider
    /// reports one.
    /// </summary>
    Task WriteAllBytesAsync(StorageNode file, byte[] content, CancellationToken ct = default);

    /// <summary>True if the document still exists; false if the provider reports it missing.</summary>
    Task<bool> ExistsAsync(StorageNode node, CancellationToken ct = default);
}
