using Android.Content;
using AndroidUri = Android.Net.Uri;

namespace SodiumStudio.Storage;

/// <summary>ACTION_OPEN_DOCUMENT / ACTION_CREATE_DOCUMENT based implementation.</summary>
public sealed class AndroidExternalFileService : IExternalFileService
{
    private static ContentResolver Resolver =>
        Microsoft.Maui.ApplicationModel.Platform.AppContext.ContentResolver
        ?? throw new StorageException(StorageErrorKind.Failed, "The Android content resolver is unavailable.");

    private static MainActivity Activity =>
        Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as MainActivity
        ?? throw new StorageException(StorageErrorKind.Failed, "No active window is available to show the file picker.");

    public async Task<PickedFile?> PickFileAsync(long maxBytes, CancellationToken ct = default)
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*"); // some providers label zip files inconsistently, so do not filter by type
        intent.PutExtra(Intent.ExtraMimeTypes, new[] { "application/zip", "application/x-zip-compressed", "application/octet-stream" });

        var result = await Activity.LaunchPickerAsync(intent).WaitAsync(ct);
        var uri = result?.Data;
        if (uri is null)
        {
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                var name = QueryDisplayName(uri) ?? "archive.zip";

                using var input = Resolver.OpenInputStream(uri)
                    ?? throw new StorageException(StorageErrorKind.Failed, "The selected file could not be opened.");
                using var buffer = new MemoryStream();
                var chunk = new byte[64 * 1024];
                int read;
                while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > maxBytes)
                    {
                        throw new StorageException(StorageErrorKind.NotSupported,
                            $"The file is larger than the {maxBytes / (1024 * 1024)} MB this build can handle.");
                    }

                    buffer.Write(chunk, 0, read);
                }

                return new PickedFile(name, buffer.ToArray());
            }
            catch (Exception ex) when (ex is not StorageException and not OperationCanceledException)
            {
                throw Translate(ex);
            }
        }, ct);
    }

    public async Task<bool> SaveFileAsync(string suggestedName, string mimeType, byte[] content, CancellationToken ct = default)
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mimeType);
        intent.PutExtra(Intent.ExtraTitle, suggestedName);

        var result = await Activity.LaunchPickerAsync(intent).WaitAsync(ct);
        var uri = result?.Data;
        if (uri is null)
        {
            return false;
        }

        await Task.Run(() =>
        {
            try
            {
                using var output = Resolver.OpenOutputStream(uri, "wt")
                    ?? throw new StorageException(StorageErrorKind.Failed, "The destination could not be opened for writing.");
                output.Write(content, 0, content.Length);
                output.Flush();
            }
            catch (Exception ex) when (ex is not StorageException and not OperationCanceledException)
            {
                throw Translate(ex);
            }
        }, ct);

        return true;
    }

    private static string? QueryDisplayName(AndroidUri uri)
    {
        // OpenableColumns.DISPLAY_NAME
        using var cursor = Resolver.Query(uri, new[] { "_display_name" }, null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return null;
        }

        var index = cursor.GetColumnIndex("_display_name");
        return index < 0 || cursor.IsNull(index) ? null : cursor.GetString(index);
    }

    private static StorageException Translate(Exception ex) => ex switch
    {
        Java.Lang.SecurityException => new StorageException(StorageErrorKind.PermissionDenied,
            "Permission to use that file was denied.", ex),
        Java.IO.FileNotFoundException => new StorageException(StorageErrorKind.NotFound,
            "The file could not be found.", ex),
        _ => new StorageException(StorageErrorKind.Failed, ex.Message, ex),
    };
}
