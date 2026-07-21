using System.Security.Cryptography;

namespace AnkiIO;

/// <summary>Describes one media payload and provides repeatable streaming access.</summary>
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
    public string FileName { get; }

    /// <summary>Gets the payload size in bytes.</summary>
    public long Length { get; }

    /// <summary>Gets the lowercase SHA-256 digest.</summary>
    public string Sha256 { get; }

    /// <summary>Opens a new readable stream. The caller owns the returned stream.</summary>
    /// <param name="cancellationToken">Cancels before the stream is opened.</param>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) => openRead(cancellationToken);
}

/// <summary>Owns media registrations for a deck and prevents unsafe or colliding names.</summary>
/// <remarks>Path-based files remain owned by the caller and must stay unchanged until serialization. Their hash is verified again by package writers.</remarks>
public sealed class AnkiMediaCollection
{
    private readonly Dictionary<string, AnkiMediaFile> files = new(StringComparer.Ordinal);

    /// <summary>Gets registered files in deterministic filename order.</summary>
    public IReadOnlyCollection<AnkiMediaFile> Files => files.Values.OrderBy(file => file.FileName, StringComparer.Ordinal).ToArray();

    /// <summary>Hashes and registers a local file without loading it into memory.</summary>
    /// <param name="path">The source file; ownership remains with the caller.</param>
    /// <param name="cancellationToken">Cancels hashing and registration.</param>
    /// <returns>The registered media descriptor.</returns>
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

    /// <summary>Registers reusable in-memory content, primarily for generated data and tests.</summary>
    /// <param name="fileName">The collection-relative filename.</param>
    /// <param name="content">Content copied on registration so caller mutation cannot affect it.</param>
    /// <returns>The registered media descriptor.</returns>
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

    /// <summary>Removes a media registration without deleting its caller-owned source.</summary>
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
