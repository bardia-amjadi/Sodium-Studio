namespace SodiumStudio.Storage;

public enum StorageErrorKind
{
    PermissionDenied,
    NotFound,
    AlreadyExists,
    NotSupported,
    InvalidName,
    Failed,
}

/// <summary>
/// The only exception type the explorer needs to understand. Android/Java exceptions
/// are translated into this inside the SAF implementation.
/// </summary>
public sealed class StorageException : Exception
{
    public StorageException(StorageErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public StorageErrorKind Kind { get; }
}
