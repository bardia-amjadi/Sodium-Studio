namespace SodiumStudio.Storage;

public sealed record PickedFile(string Name, byte[] Content);

/// <summary>
/// Single files OUTSIDE the project folder, chosen by the user through the system file pickers
/// (used for importing and exporting zip archives). Each call shows a system dialog; the app gets
/// access to that one file only and needs no storage permission.
/// </summary>
public interface IExternalFileService
{
    /// <summary>Asks the user for a file and reads it. Returns null if cancelled. Throws if larger than <paramref name="maxBytes"/>.</summary>
    Task<PickedFile?> PickFileAsync(long maxBytes, CancellationToken ct = default);

    /// <summary>Asks the user where to save <paramref name="content"/>. Returns false if cancelled.</summary>
    Task<bool> SaveFileAsync(string suggestedName, string mimeType, byte[] content, CancellationToken ct = default);
}
