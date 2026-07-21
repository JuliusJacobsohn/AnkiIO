using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AnkiIO;

/// <summary>Writes guarded legacy <c>.apkg</c> package representations accepted by the Anki 26.05 importer.</summary>
/// <remarks>
/// Output is a ZIP archive containing <c>collection.anki2</c> with schema-11 JSON model/deck metadata, a fixed legacy
/// scheduler-version-2 collection configuration, and the traditional numeric-entry media map. Anki 26.05 has been
/// verified to import this backward-compatible representation. This writer does not emit Anki 26.05's native modern
/// <c>collection.anki21b</c>, schema-18 protobuf metadata, or <c>meta</c> entries, and it does not claim byte-for-byte
/// compatibility with packages exported by Anki 26.05.
/// <para>
/// Deck overloads write one supplied root and all descendants. Package overloads write every top-level hierarchy and
/// combine package-level media with media registered on any traversed deck. A fixed default deck configuration is emitted;
/// deck options, <see cref="AnkiDeck.Metadata"/>, <see cref="AnkiDeck.UnknownData"/>, and unsupported storage fields are
/// not preserved. Review-log rows are written, but <see cref="AnkiReviewLog.ReviewedAt"/> is not independently encoded;
/// the numeric <see cref="AnkiReviewLog.Id"/> is written as the legacy review-log key.
/// </para>
/// <para>
/// Duplicate case-sensitive media filenames with identical length and SHA-256 are coalesced. A duplicate filename with
/// conflicting content is rejected before output is opened. This type has no mutable global state, so separate writes may
/// run concurrently. Supplied package graphs, note types, cards, review histories, media collections, and path-backed
/// media files must not be mutated during a write. Collection modification timestamps are generated at write time, so
/// repeated writes are not byte-for-byte deterministic.
/// </para>
/// </remarks>
public static class AnkiPackageWriter
{
    /// <summary>Writes one deck hierarchy to a package file without opening or modifying an Anki profile.</summary>
    /// <param name="deck">The single root deck whose complete descendant hierarchy will be written.</param>
    /// <param name="path">The destination package path. The extension is not validated.</param>
    /// <param name="cancellationToken">Cancels database creation and asynchronous archive or media I/O.</param>
    /// <returns>A task that completes after the archive has been finalized and committed to the destination path.</returns>
    /// <remarks>
    /// The hierarchy and media-name collisions are validated before any destination or temporary output file is opened.
    /// Output is built in a uniquely named temporary file in the destination directory, then committed with a same-directory
    /// replacement move. An existing destination remains unchanged if validation, database creation, archive creation,
    /// media verification, or cancellation fails before that move. Parent directories are not created. Temporary output is
    /// removed on failure. Path-backed media is streamed and SHA-256 verified against its registration-time digest.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="deck"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank or has invalid path syntax.</exception>
    /// <exception cref="PathTooLongException"><paramref name="path"/> or a registered media path exceeds a platform path-length limit.</exception>
    /// <exception cref="DirectoryNotFoundException">The destination parent or a registered media directory does not exist.</exception>
    /// <exception cref="FileNotFoundException">A path-backed media source no longer exists.</exception>
    /// <exception cref="UnauthorizedAccessException">The destination, a media source, or the temporary workspace cannot be accessed.</exception>
    /// <exception cref="AnkiValidationException"><paramref name="deck"/> violates a checked domain invariant.</exception>
    /// <exception cref="InvalidDataException">A path-backed media payload no longer matches its registration-time SHA-256 digest.</exception>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">The temporary legacy collection database cannot be created or populated.</exception>
    /// <exception cref="InvalidOperationException">Media filenames collide with different content, or mutable input changes during enumeration.</exception>
    /// <exception cref="IOException">The destination, media, or temporary workspace cannot be read, written, finalized, replaced, or removed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task WriteAsync(AnkiDeck deck, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var plan = CreateWritePlan([deck], packageMedia: null);
        await WritePathAsync(plan, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes every hierarchy and all retained media in a previously read package to a package file.</summary>
    /// <param name="package">The package whose top-level deck hierarchies and package-level media will be written.</param>
    /// <param name="path">The destination package path. The extension is not validated.</param>
    /// <param name="cancellationToken">Cancels database creation and asynchronous archive or media I/O.</param>
    /// <returns>A task that completes after the archive has been finalized and committed to the destination path.</returns>
    /// <remarks>
    /// Use this overload for lossless supported-media read-modify-write workflows. It includes every registration in
    /// <see cref="AnkiPackage.Media"/> as well as registrations added to any deck in <see cref="AnkiPackage.Decks"/>.
    /// Identical duplicate registrations are coalesced and conflicting registrations are rejected before output is opened.
    /// The path is written through a same-directory temporary file and replacement move; an existing destination remains
    /// unchanged on any failure before the final move, and temporary output is removed on failure.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="package"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="package"/> contains no top-level decks, or <paramref name="path"/> is blank or invalid.</exception>
    /// <exception cref="PathTooLongException"><paramref name="path"/> or a registered media path exceeds a platform path-length limit.</exception>
    /// <exception cref="DirectoryNotFoundException">The destination parent or a registered media directory does not exist.</exception>
    /// <exception cref="FileNotFoundException">A path-backed media source no longer exists.</exception>
    /// <exception cref="UnauthorizedAccessException">The destination, a media source, or the temporary workspace cannot be accessed.</exception>
    /// <exception cref="AnkiValidationException">A hierarchy in <paramref name="package"/> violates a checked domain invariant.</exception>
    /// <exception cref="InvalidDataException">A path-backed media payload no longer matches its registration-time SHA-256 digest.</exception>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">The temporary legacy collection database cannot be created or populated.</exception>
    /// <exception cref="InvalidOperationException">Media filenames collide with different content, or mutable input changes during enumeration.</exception>
    /// <exception cref="IOException">The destination, media, or temporary workspace cannot be read, written, finalized, replaced, or removed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task WriteAsync(AnkiPackage package, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var plan = CreatePackageWritePlan(package);
        await WritePathAsync(plan, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes one deck hierarchy to a writable caller-owned stream and leaves the stream open.</summary>
    /// <param name="deck">The single root deck whose complete descendant hierarchy will be written.</param>
    /// <param name="destination">A writable stream positioned where the ZIP archive should begin.</param>
    /// <param name="cancellationToken">Cancels database creation and asynchronous archive or media I/O.</param>
    /// <returns>A task that completes after the archive and its central directory have been finalized.</returns>
    /// <remarks>
    /// The caller retains ownership of <paramref name="destination"/>. Seeking is not required, but the stream must be
    /// writable and must not be accessed concurrently. Existing content is not truncated, the initial position is not
    /// restored, and a failure or cancellation can leave partial archive bytes. For a reusable seekable stream, callers
    /// should position and truncate it before writing. The destination is not touched when domain or media-collision
    /// validation fails. Path-backed media is streamed and SHA-256 verified against its registration-time digest.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="deck"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="destination"/> or a path-backed media stream has been disposed.</exception>
    /// <exception cref="AnkiValidationException"><paramref name="deck"/> violates a checked domain invariant.</exception>
    /// <exception cref="FileNotFoundException">A path-backed media source no longer exists.</exception>
    /// <exception cref="DirectoryNotFoundException">A registered media directory does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">A media source or the temporary workspace cannot be accessed.</exception>
    /// <exception cref="InvalidDataException">A path-backed media payload no longer matches its registration-time SHA-256 digest.</exception>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">The temporary legacy collection database cannot be created or populated.</exception>
    /// <exception cref="InvalidOperationException">Media filenames collide with different content, or mutable input changes during enumeration.</exception>
    /// <exception cref="NotSupportedException"><paramref name="destination"/> does not support a required write operation.</exception>
    /// <exception cref="IOException">The destination, media, or temporary workspace cannot be read, written, finalized, or removed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task WriteAsync(AnkiDeck deck, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(destination);
        EnsureWritable(destination);
        var plan = CreateWritePlan([deck], packageMedia: null);
        await WriteArchiveAsync(plan, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes every hierarchy and all retained media in a package to a caller-owned stream and leaves it open.</summary>
    /// <param name="package">The package whose top-level deck hierarchies and package-level media will be written.</param>
    /// <param name="destination">A writable stream positioned where the ZIP archive should begin.</param>
    /// <param name="cancellationToken">Cancels database creation and asynchronous archive or media I/O.</param>
    /// <returns>A task that completes after the archive and its central directory have been finalized.</returns>
    /// <remarks>
    /// Use this overload for lossless supported-media read-modify-write workflows. Every registration in
    /// <see cref="AnkiPackage.Media"/> is combined with media registered on the package's deck hierarchies. The caller
    /// retains ownership of <paramref name="destination"/>. Seeking is not required. Validation occurs before the stream is
    /// written, but later failure or cancellation can leave partial archive bytes in the caller-owned stream.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="package"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="package"/> contains no top-level decks, or <paramref name="destination"/> is not writable.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="destination"/> or a path-backed media stream has been disposed.</exception>
    /// <exception cref="AnkiValidationException">A hierarchy in <paramref name="package"/> violates a checked domain invariant.</exception>
    /// <exception cref="FileNotFoundException">A path-backed media source no longer exists.</exception>
    /// <exception cref="DirectoryNotFoundException">A registered media directory does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">A media source or the temporary workspace cannot be accessed.</exception>
    /// <exception cref="InvalidDataException">A path-backed media payload no longer matches its registration-time SHA-256 digest.</exception>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">The temporary legacy collection database cannot be created or populated.</exception>
    /// <exception cref="InvalidOperationException">Media filenames collide with different content, or mutable input changes during enumeration.</exception>
    /// <exception cref="NotSupportedException"><paramref name="destination"/> does not support a required write operation.</exception>
    /// <exception cref="IOException">The destination, media, or temporary workspace cannot be read, written, finalized, or removed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task WriteAsync(AnkiPackage package, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(destination);
        EnsureWritable(destination);
        var plan = CreatePackageWritePlan(package);
        await WriteArchiveAsync(plan, destination, cancellationToken).ConfigureAwait(false);
    }

    private static WritePlan CreatePackageWritePlan(AnkiPackage package)
    {
        var roots = package.Decks.ToArray();
        if (roots.Length == 0)
        {
            throw new ArgumentException("A package must contain at least one top-level deck.", nameof(package));
        }

        return CreateWritePlan(roots, package.Media.Files);
    }

    private static WritePlan CreateWritePlan(IReadOnlyList<AnkiDeck> roots, IEnumerable<AnkiMediaFile>? packageMedia)
    {
        var diagnostics = roots.SelectMany(root => AnkiValidator.Validate(root).Diagnostics).ToArray();
        var validation = new AnkiValidationResult(diagnostics);
        if (!validation.IsValid)
        {
            throw new AnkiValidationException(validation);
        }

        var mediaByName = new Dictionary<string, AnkiMediaFile>(StringComparer.Ordinal);
        if (packageMedia is not null)
        {
            foreach (var media in packageMedia)
            {
                AddMedia(mediaByName, media);
            }
        }

        foreach (var media in roots.SelectMany(root => root.Traverse()).SelectMany(deck => deck.Media.Files))
        {
            AddMedia(mediaByName, media);
        }

        return new WritePlan(roots, mediaByName.Values.OrderBy(media => media.FileName, StringComparer.Ordinal).ToArray());
    }

    private static void AddMedia(Dictionary<string, AnkiMediaFile> mediaByName, AnkiMediaFile media)
    {
        if (!mediaByName.TryGetValue(media.FileName, out var existing))
        {
            mediaByName.Add(media.FileName, media);
            return;
        }

        if (existing.Length != media.Length || !string.Equals(existing.Sha256, media.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Media filename '{media.FileName}' is registered with conflicting content.");
        }
    }

    private static void EnsureWritable(Stream destination)
    {
        if (!destination.CanWrite)
        {
            throw new ArgumentException("Package output must be writable.", nameof(destination));
        }
    }

    private static async Task WritePathAsync(WritePlan plan, string path, CancellationToken cancellationToken)
    {
        var destinationPath = Path.GetFullPath(path);
        var destinationDirectory = Path.GetDirectoryName(destinationPath) ?? Directory.GetCurrentDirectory();
        var temporaryPath = Path.Combine(destinationDirectory, ".AnkiIO-write-" + Guid.NewGuid().ToString("N") + ".tmp");
        var committed = false;
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await WriteArchiveAsync(plan, stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath, overwrite: true);
            committed = true;
        }
        finally
        {
            if (!committed && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteArchiveAsync(WritePlan plan, Stream destination, CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "AnkiIO-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var databasePath = Path.Combine(tempDirectory, "collection.anki2");
            await LegacyCollectionDatabase.WriteAsync(databasePath, plan.Roots, cancellationToken).ConfigureAwait(false);
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            var collectionEntry = archive.CreateEntry("collection.anki2", CompressionLevel.Optimal);
            await using (var entryStream = collectionEntry.Open())
            await using (var database = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await database.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
            }

            var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < plan.Media.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = plan.Media[index];
                var entryName = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                map.Add(entryName, item.FileName);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                await using var destinationMedia = entry.Open();
                await using var sourceMedia = await item.OpenReadAsync(cancellationToken).ConfigureAwait(false);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = await sourceMedia.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await destinationMedia.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                var actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actual, item.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Media '{item.FileName}' changed after registration; expected SHA-256 {item.Sha256}, got {actual}.");
                }
            }

            var mediaEntry = archive.CreateEntry("media", CompressionLevel.Optimal);
            await using var mediaStream = mediaEntry.Open();
            await JsonSerializer.SerializeAsync(mediaStream, map, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private sealed record WritePlan(IReadOnlyList<AnkiDeck> Roots, IReadOnlyList<AnkiMediaFile> Media);
}
