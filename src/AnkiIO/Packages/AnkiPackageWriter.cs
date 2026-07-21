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
/// One supplied root and all descendants are written. A fixed default deck configuration is emitted; deck options,
/// <see cref="AnkiDeck.Metadata"/>, <see cref="AnkiDeck.UnknownData"/>, and unsupported storage fields are not preserved.
/// Review-log rows are written, but <see cref="AnkiReviewLog.ReviewedAt"/> is not independently encoded; the numeric
/// <see cref="AnkiReviewLog.Id"/> is written as the legacy review-log key. Media registrations are gathered from every
/// traversed deck. If several decks register the same case-sensitive filename, only the first descriptor in traversal
/// order is packaged, without comparing later registrations.
/// </para>
/// <para>
/// This type has no mutable global state, so separate writes may run concurrently. The supplied hierarchy, note types,
/// cards, review histories, media collections, and path-backed media files must not be mutated during a write.
/// Collection modification timestamps are generated at write time, so repeated writes are not byte-for-byte deterministic.
/// </para>
/// </remarks>
public static class AnkiPackageWriter
{
    /// <summary>Writes a deck hierarchy to a package file without opening or modifying an Anki profile.</summary>
    /// <param name="deck">The single root deck whose complete descendant hierarchy will be written.</param>
    /// <param name="path">The destination package path. The extension is not validated.</param>
    /// <param name="cancellationToken">Cancels database creation and asynchronous archive or media I/O.</param>
    /// <returns>A task that completes after the archive and its central directory have been finalized.</returns>
    /// <remarks>
    /// The destination is opened with <see cref="FileMode.Create"/> before <paramref name="deck"/> is validated, so an
    /// existing file is truncated immediately and a missing file is created. Parent directories are not created. Failure
    /// or cancellation can leave a partial package at <paramref name="path"/>; the method does not restore or delete a
    /// previous destination. The writer uses and then attempts to remove an isolated temporary database directory.
    /// Path-backed media is streamed and SHA-256 verified against its registration-time digest before completion.
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
    /// <exception cref="InvalidOperationException">The hierarchy or one of its mutable collections changes during enumeration.</exception>
    /// <exception cref="IOException">The destination, media, or temporary workspace cannot be read, written, finalized, or removed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task WriteAsync(AnkiDeck deck, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await WriteAsync(deck, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a package to a writable caller-owned stream and leaves the stream open.</summary>
    /// <param name="deck">The single root deck whose complete descendant hierarchy will be written.</param>
    /// <param name="destination">A writable stream positioned where the ZIP archive should begin.</param>
    /// <param name="cancellationToken">Cancels database creation and asynchronous archive or media I/O.</param>
    /// <returns>A task that completes after the archive and its central directory have been finalized.</returns>
    /// <remarks>
    /// The caller retains ownership of <paramref name="destination"/>. Seeking is not required, but the stream must be
    /// writable and must not be accessed concurrently. Existing content is not truncated, the initial position is not
    /// restored, and a failure or cancellation can leave partial archive bytes. For a reusable seekable stream, callers
    /// should position and truncate it before writing. The destination is not touched when domain validation fails in this
    /// overload. Path-backed media is streamed and SHA-256 verified against its registration-time digest.
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
    /// <exception cref="InvalidOperationException">The hierarchy or one of its mutable collections changes during enumeration.</exception>
    /// <exception cref="NotSupportedException"><paramref name="destination"/> does not support a required write operation.</exception>
    /// <exception cref="IOException">The destination, media, or temporary workspace cannot be read, written, finalized, or removed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task WriteAsync(AnkiDeck deck, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(destination);
        var validation = AnkiValidator.Validate(deck);
        if (!validation.IsValid)
        {
            throw new AnkiValidationException(validation);
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "AnkiIO-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var databasePath = Path.Combine(tempDirectory, "collection.anki2");
            await LegacyCollectionDatabase.WriteAsync(databasePath, deck, cancellationToken).ConfigureAwait(false);
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            var collectionEntry = archive.CreateEntry("collection.anki2", CompressionLevel.Optimal);
            await using (var entryStream = collectionEntry.Open())
            await using (var database = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await database.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
            }

            var media = deck.Traverse().SelectMany(value => value.Media.Files).GroupBy(file => file.FileName, StringComparer.Ordinal).Select(group => group.First()).OrderBy(file => file.FileName, StringComparer.Ordinal).ToArray();
            var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < media.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = media[index];
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
}
