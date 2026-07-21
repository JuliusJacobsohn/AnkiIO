using System.Security.Cryptography;

namespace AnkiIO;

/// <summary>Describes one registered Anki media payload and provides repeatable streaming access to it.</summary>
/// <remarks>
/// Instances are immutable descriptors. Each call to <see cref="OpenReadAsync"/> returns an independent stream, so
/// callers may read the same descriptor concurrently. For path-backed registrations, the source file remains owned by
/// the caller and may disappear or change after registration; <see cref="Length"/> and <see cref="Sha256"/> describe
/// the content observed when it was registered. Package writing rereads the source and rejects changed content.
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
    /// <value>A simple filename without a directory component or path separator.</value>
    public string FileName { get; }

    /// <summary>Gets the payload size in bytes.</summary>
    /// <value>The length observed when the payload was registered.</value>
    public long Length { get; }

    /// <summary>Gets the lowercase SHA-256 digest.</summary>
    /// <value>The 64-character lowercase hexadecimal digest of the content observed at registration time.</value>
    public string Sha256 { get; }

    /// <summary>Opens a new readable stream positioned at the beginning of the registered payload.</summary>
    /// <param name="cancellationToken">Cancels the operation before the stream is opened.</param>
    /// <returns>A task-like operation whose result is a new readable stream owned by the caller.</returns>
    /// <remarks>
    /// The caller must dispose the returned stream. Disposing it does not remove the registration. Opening a path-backed
    /// descriptor accesses the original path at call time; this method does not itself compare the current content with
    /// <see cref="Sha256"/>.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before opening.</exception>
    /// <exception cref="FileNotFoundException">The path-backed source no longer exists.</exception>
    /// <exception cref="DirectoryNotFoundException">A path-backed source directory no longer exists.</exception>
    /// <exception cref="PathTooLongException">The retained source path exceeds a platform path-length limit.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller cannot read a path-backed source.</exception>
    /// <exception cref="IOException">The underlying source cannot be opened for reading.</exception>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) => openRead(cancellationToken);
}

/// <summary>Owns media registrations for a deck and prevents unsafe or colliding names.</summary>
/// <remarks>
/// This collection is mutable and is not safe for concurrent mutation. Filenames use ordinal, case-sensitive comparison;
/// callers should nevertheless avoid names that differ only by case because Anki media directories may reside on a
/// case-insensitive filesystem. Path-backed files remain owned by the caller and must remain readable and unchanged until
/// serialization. Byte-backed registrations copy their input and own that copy. A package reader returns extracted media
/// through <see cref="AnkiPackage.Media"/>; it does not automatically attach those registrations to any deck's
/// <see cref="AnkiDeck.Media"/> collection.
/// </remarks>
public sealed class AnkiMediaCollection
{
    private readonly Dictionary<string, AnkiMediaFile> files = new(StringComparer.Ordinal);

    /// <summary>Initializes an empty media collection.</summary>
    /// <remarks>The new collection owns no external resource and requires no disposal.</remarks>
    public AnkiMediaCollection()
    {
    }

    /// <summary>Gets a snapshot of registered files in deterministic filename order.</summary>
    /// <value>
    /// A newly allocated, read-only view ordered by <see cref="AnkiMediaFile.FileName"/> using ordinal comparison.
    /// </value>
    public IReadOnlyCollection<AnkiMediaFile> Files => Array.AsReadOnly(files.Values.OrderBy(file => file.FileName, StringComparer.Ordinal).ToArray());

    /// <summary>Hashes and registers a local file without loading it into memory.</summary>
    /// <param name="path">
    /// The source path. Only its final filename is used as the Anki media name; ownership of the file remains with the caller.
    /// </param>
    /// <param name="cancellationToken">Cancels asynchronous hashing before the registration is added.</param>
    /// <returns>
    /// A descriptor for the supplied path and its registration-time content. If identical content is already registered
    /// under the same filename, the collection remains unchanged.
    /// </returns>
    /// <remarks>
    /// Content is streamed while hashing, but the source path is retained rather than copied. The file must remain
    /// readable and unchanged until every operation that consumes the registration has completed. Registering the same
    /// filename and digest is idempotent; registering the same filename with a different digest is rejected.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is blank, resolves without a filename, or its filename is unsafe for Anki media.
    /// </exception>
    /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
    /// <exception cref="DirectoryNotFoundException">A source directory does not exist.</exception>
    /// <exception cref="PathTooLongException"><paramref name="path"/> exceeds a platform path-length limit.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller cannot read the source file.</exception>
    /// <exception cref="IOException">The source cannot be opened or read.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled while hashing.</exception>
    /// <exception cref="InvalidOperationException">
    /// The filename is already registered with different content.
    /// </exception>
    public async Task<AnkiMediaFile> AddFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var name = Path.GetFileName(fullPath);
        ValidateFileName(name);

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        var media = new AnkiMediaFile(name, stream.Length, hash, token =>
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan));
        });
        AddChecked(media);
        return media;
    }

    /// <summary>Copies and registers reusable in-memory content.</summary>
    /// <param name="fileName">A simple collection-relative filename without directory components.</param>
    /// <param name="content">Content copied on registration so caller mutation cannot affect it.</param>
    /// <returns>
    /// A descriptor for the copied content. If identical content is already registered under the same filename, the
    /// collection remains unchanged.
    /// </returns>
    /// <remarks>
    /// Both the input and its SHA-256 digest are processed synchronously. Registering the same filename and digest is
    /// idempotent; registering the same filename with different content is rejected.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fileName"/> is blank, rooted, a dot segment, contains a path separator, or contains a platform-invalid filename character.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The filename is already registered with different content.
    /// </exception>
    public AnkiMediaFile AddBytes(string fileName, ReadOnlySpan<byte> content)
    {
        ValidateFileName(fileName);
        var bytes = content.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var media = new AnkiMediaFile(fileName, bytes.LongLength, hash, token =>
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        });
        AddChecked(media);
        return media;
    }

    /// <summary>Removes a media registration without deleting or invalidating its source content.</summary>
    /// <param name="fileName">The case-sensitive registered filename to remove.</param>
    /// <returns><see langword="true"/> when a registration was removed; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// Existing <see cref="AnkiMediaFile"/> descriptors and streams remain usable. Path-backed files are never deleted,
    /// and byte-backed content already referenced by a descriptor remains owned by that descriptor.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is <see langword="null"/>.</exception>
    public bool Remove(string fileName) => files.Remove(fileName);

    internal static void ValidateFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (Path.IsPathRooted(fileName) || fileName is "." or ".." || fileName.Contains('/') || fileName.Contains('\\') || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Media names must be simple, non-rooted filenames without path separators.", nameof(fileName));
        }
    }

    private void AddChecked(AnkiMediaFile media)
    {
        if (files.TryGetValue(media.FileName, out var existing))
        {
            if (!string.Equals(existing.Sha256, media.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Media filename '{media.FileName}' has colliding content.");
            }

            return;
        }

        files.Add(media.FileName, media);
    }
}
