namespace AnkiIO;

/// <summary>Describes one registered Anki media payload and provides repeatable streaming access to it.</summary>
/// <remarks>
/// <para>
/// A descriptor is immutable and does not itself own an open stream, so it does not require disposal. Each
/// <see cref="OpenReadAsync"/> call creates an independent stream positioned at the beginning; the caller owns and must
/// dispose that returned stream. Cancellation applies while opening, not to later reads from the returned stream.
/// </para>
/// <para>
/// A descriptor created by <see cref="AnkiMediaCollection.AddBytes"/> owns an eager defensive copy of the bytes and can be
/// reopened after the caller changes or releases its input buffer. A descriptor created by
/// <see cref="AnkiMediaCollection.AddFileAsync"/> retains the source path instead. The caller must keep that file readable
/// and unchanged until all package writes are complete.
/// </para>
/// <para>
/// <see cref="Length"/> and <see cref="Sha256"/> describe registration-time content. Opening a path-backed descriptor does
/// not revalidate those values, but package writing hashes the emitted bytes and rejects a changed source rather than
/// silently producing a package inconsistent with the descriptor.
/// </para>
/// </remarks>
public sealed class AnkiMediaFile
{
    private readonly Func<CancellationToken, ValueTask<Stream>> openRead;

    internal AnkiMediaFile(string fileName, long length, string sha256, Func<CancellationToken, ValueTask<Stream>> openRead)
    {
        FileName = fileName;
        Length = length;
        Sha256 = sha256;
        this.openRead = openRead;
    }

    /// <summary>Gets the safe collection-relative filename referenced by note HTML or sound markup.</summary>
    /// <value>A portable simple filename without a directory component or path separator.</value>
    public string FileName { get; }

    /// <summary>Gets the payload size observed during registration.</summary>
    /// <value>The byte length of the registered content, including zero for an empty payload.</value>
    public long Length { get; }

    /// <summary>Gets the registration-time SHA-256 digest.</summary>
    /// <value>The 64-character lowercase hexadecimal digest of the registered content.</value>
    public string Sha256 { get; }

    /// <summary>Opens a new readable stream positioned at the beginning of the registered payload.</summary>
    /// <param name="cancellationToken">Cancels the operation before the stream is opened.</param>
    /// <returns>A task-like operation whose result is a new readable stream owned by the caller.</returns>
    /// <remarks>
    /// The caller must dispose the returned stream. Multiple streams from the same descriptor have independent positions
    /// and may be consumed concurrently. For path-backed media, this accesses the retained source path at call time and
    /// therefore may fail if the caller moved, removed, or made the file unreadable after registration.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before opening.</exception>
    /// <exception cref="FileNotFoundException">The path-backed source no longer exists.</exception>
    /// <exception cref="DirectoryNotFoundException">A path-backed source directory no longer exists.</exception>
    /// <exception cref="PathTooLongException">The retained source path exceeds a platform path-length limit.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller cannot read a path-backed source.</exception>
    /// <exception cref="IOException">The underlying source cannot be opened for reading.</exception>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) => openRead(cancellationToken);
}
