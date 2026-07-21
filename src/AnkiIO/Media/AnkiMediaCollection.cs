using System.Security.Cryptography;

namespace AnkiIO;

/// <summary>Owns media registrations for a deck and prevents unsafe or colliding names.</summary>
/// <remarks>
/// <para>
/// Choose <see cref="AddBytes"/> when the collection should own a stable, reusable payload. It eagerly copies the entire
/// input into managed memory. Choose <see cref="AddFileAsync"/> for large assets: hashing is streamed, but the source path
/// remains caller-owned and must stay readable and unchanged through serialization. Package reading also registers
/// extracted media as byte-backed payloads and therefore has memory use proportional to extracted media size.
/// </para>
/// <para>
/// A filename and SHA-256 digest form the registration identity. Repeating the same name and content is idempotent and
/// returns the descriptor already stored in the collection. Reusing the name for different content throws, preventing a
/// template reference from becoming ambiguous. Content may be shared by different filenames.
/// </para>
/// <para>
/// Names are validated against a portable subset usable on Windows, macOS, Linux, and in a ZIP package. Directory
/// components, control and reserved characters, Windows device names, and trailing spaces or periods are rejected even
/// when the current operating system would accept them. Comparison remains ordinal and case-sensitive; avoid names that
/// differ only by case when packages may be extracted on a case-insensitive filesystem.
/// </para>
/// <para>
/// The collection is mutable and is not safe for concurrent mutation. <see cref="Files"/> returns a detached, read-only,
/// deterministically ordered snapshot. A package reader exposes extracted media through <see cref="AnkiPackage.Media"/>;
/// it does not attach those registrations to the <see cref="AnkiDeck.Media"/> collection of each imported deck.
/// </para>
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
    /// A newly allocated, read-only view ordered by <see cref="AnkiMediaFile.FileName"/> using ordinal comparison. Later
    /// registrations and removals do not alter a previously retrieved snapshot.
    /// </value>
    public IReadOnlyCollection<AnkiMediaFile> Files => Array.AsReadOnly(files.Values.OrderBy(file => file.FileName, StringComparer.Ordinal).ToArray());

    /// <summary>Hashes and registers a local file without loading it into memory.</summary>
    /// <param name="path">
    /// The source path. Only its final filename is used as the Anki media name; ownership of the file remains with the caller.
    /// </param>
    /// <param name="cancellationToken">Cancels asynchronous hashing before the registration is added.</param>
    /// <returns>
    /// The stored descriptor for the supplied filename and content. If identical content is already registered under the
    /// same filename, the collection remains unchanged and its existing descriptor is returned.
    /// </returns>
    /// <remarks>
    /// Content is streamed once to compute SHA-256, but the source path is retained rather than copied. Keep the file
    /// readable and unchanged until every package write consuming this registration completes. A pre-canceled or
    /// mid-hash cancellation leaves the collection unchanged.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is blank, resolves without a filename, or its filename is unsafe for portable Anki media.
    /// </exception>
    /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
    /// <exception cref="DirectoryNotFoundException">A source directory does not exist.</exception>
    /// <exception cref="PathTooLongException"><paramref name="path"/> exceeds a platform path-length limit.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller cannot read the source file.</exception>
    /// <exception cref="IOException">The source cannot be opened or read.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled while hashing.</exception>
    /// <exception cref="InvalidOperationException">The filename is already registered with different content.</exception>
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
        return AddChecked(media);
    }

    /// <summary>Copies and registers reusable in-memory content.</summary>
    /// <param name="fileName">A portable simple filename without directory components.</param>
    /// <param name="content">Content copied on registration so caller mutation cannot affect it.</param>
    /// <returns>
    /// The stored descriptor for the copied content. If identical content is already registered under the same filename,
    /// the collection remains unchanged and its existing descriptor is returned.
    /// </returns>
    /// <remarks>
    /// Copying and SHA-256 hashing are synchronous and eager, including for large inputs. Empty payloads are valid. Each
    /// stream later opened from the descriptor reads the owned copy and is unaffected by mutation of the caller's buffer.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fileName"/> is blank, rooted, a dot segment, a Windows reserved device name, ends in a space or
    /// period, or contains a path separator, control character, or portable-invalid filename character.
    /// </exception>
    /// <exception cref="InvalidOperationException">The filename is already registered with different content.</exception>
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
        return AddChecked(media);
    }

    /// <summary>Removes a media registration without deleting or invalidating its source content.</summary>
    /// <param name="fileName">The case-sensitive registered filename to remove.</param>
    /// <returns><see langword="true"/> when a registration was removed; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// Existing descriptors and streams remain usable. Path-backed files are never deleted, and byte-backed content
    /// remains owned by any descriptor that still references it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is <see langword="null"/>.</exception>
    public bool Remove(string fileName) => files.Remove(fileName);

    internal static void ValidateFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (Path.IsPathRooted(fileName)
            || fileName is "." or ".."
            || fileName.EndsWith(' ')
            || fileName.EndsWith('.')
            || fileName.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character, StringComparison.Ordinal))
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || IsWindowsReservedName(fileName))
        {
            throw new ArgumentException("Media names must be portable simple filenames without reserved names, path separators, control characters, or trailing spaces and periods.", nameof(fileName));
        }
    }

    private static bool IsWindowsReservedName(string fileName)
    {
        var period = fileName.IndexOf('.');
        var stem = period < 0 ? fileName : fileName[..period];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && stem[3] is >= '1' and <= '9';
    }

    private AnkiMediaFile AddChecked(AnkiMediaFile media)
    {
        if (files.TryGetValue(media.FileName, out var existing))
        {
            if (!string.Equals(existing.Sha256, media.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Media filename '{media.FileName}' has colliding content.");
            }

            return existing;
        }

        files.Add(media.FileName, media);
        return media;
    }
}
