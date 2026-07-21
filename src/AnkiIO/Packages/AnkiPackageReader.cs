using System.IO.Compression;
using System.Text.Json;

namespace AnkiIO;

/// <summary>Reads guarded legacy <c>.apkg</c> package representations accepted by Anki 26.05.</summary>
/// <remarks>
/// This reader treats every archive, media map, and SQLite database as untrusted input and applies
/// <see cref="AnkiPackageLimits"/> before extraction. It supports a ZIP archive containing
/// <c>collection.anki2</c> with legacy JSON model and deck metadata. It accepts collection schema values 11 and 18 only
/// when that metadata remains in the legacy JSON representation. It does not decode modern schema-18 protobuf metadata,
/// <c>collection.anki21</c>, <c>collection.anki21b</c>, or <c>meta</c> package entries commonly produced by current Anki.
/// Anki 26.05 can import the legacy representation, but that acceptance does not make it Anki's native current format.
/// <para>
/// Supported decks, notes, front/back templates, cards, scheduling fields, low three-bit card flags, descriptions, and
/// media are reconstructed. Review-log rows, deck configuration, graves, unknown SQLite columns, note/card auxiliary
/// columns, and unsupported model metadata are not preserved. Supported field editor settings and browser-only template
/// formats are retained.
/// Media payloads are eagerly copied into memory and returned through
/// <see cref="AnkiPackage.Media"/> rather than attached to individual decks. Calls are safe to run concurrently when each
/// call receives its own stream; returned mutable package graphs are not safe for concurrent mutation.
/// </para>
/// </remarks>
public static class AnkiPackageReader
{
    /// <summary>Reads a package file without modifying the source file or an Anki profile.</summary>
    /// <param name="path">The path of the legacy-compatible <c>.apkg</c> archive to read.</param>
    /// <param name="limits">
    /// Optional archive defenses. <see langword="null"/> selects <see cref="AnkiPackageLimits.Default"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels extraction, database reads, media copying, and JSON deserialization.</param>
    /// <returns>A task whose result owns the supported deck graph, diagnostics, and copied media registrations.</returns>
    /// <remarks>
    /// The source is opened read-only and closed before the returned task completes. The method attempts to remove
    /// temporary extracted database content on success, cancellation, or failure. Cancellation is observed by asynchronous
    /// I/O and database work; synchronous ZIP structure validation and in-memory media hashing are not individually cancelable.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank or has invalid path syntax.</exception>
    /// <exception cref="PathTooLongException"><paramref name="path"/> exceeds a platform path-length limit.</exception>
    /// <exception cref="FileNotFoundException">The package file does not exist.</exception>
    /// <exception cref="DirectoryNotFoundException">A path directory does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The package or temporary workspace cannot be accessed.</exception>
    /// <exception cref="AnkiPackageSecurityException">The archive violates a configured size, ratio, count, path, link, or media-map safety rule.</exception>
    /// <exception cref="NotSupportedException">The archive lacks supported legacy collection data or uses unsupported collection metadata.</exception>
    /// <exception cref="InvalidDataException">The ZIP data or a supported package relationship is malformed.</exception>
    /// <exception cref="JsonException">The media map or legacy JSON metadata is malformed.</exception>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">The extracted collection is not a readable supported SQLite database.</exception>
    /// <exception cref="InvalidOperationException">Malformed content creates a conflicting media registration or invalid object relationship.</exception>
    /// <exception cref="OutOfMemoryException">The process cannot allocate memory for an extracted media payload.</exception>
    /// <exception cref="OverflowException">The aggregate uncompressed archive length cannot be represented by <see cref="long"/>.</exception>
    /// <exception cref="IOException">A package, temporary file, or temporary directory cannot be read, written, or removed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<AnkiPackage> ReadAsync(string path, AnkiPackageLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadAsync(stream, limits, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a package from a readable, seekable caller-owned stream and leaves it open.</summary>
    /// <param name="source">A readable, seekable stream containing the complete package archive.</param>
    /// <param name="limits">
    /// Optional archive defenses. <see langword="null"/> selects <see cref="AnkiPackageLimits.Default"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels extraction, database reads, media copying, and JSON deserialization.</param>
    /// <returns>A task whose result owns the supported deck graph, diagnostics, and copied media registrations.</returns>
    /// <remarks>
    /// The caller retains ownership of <paramref name="source"/>. The method does not restore its original position, and
    /// callers must not access or reposition it concurrently while reading is in progress. The method attempts to remove
    /// temporary extracted database content on success, cancellation, or failure. Cancellation is observed by asynchronous
    /// I/O and database work; synchronous ZIP structure validation and in-memory media hashing are not individually cancelable.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not readable and seekable.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> has been disposed.</exception>
    /// <exception cref="AnkiPackageSecurityException">The archive violates a configured size, ratio, count, path, link, or media-map safety rule.</exception>
    /// <exception cref="NotSupportedException">The archive lacks supported legacy collection data or uses unsupported collection metadata.</exception>
    /// <exception cref="InvalidDataException">The ZIP data or a supported package relationship is malformed.</exception>
    /// <exception cref="JsonException">The media map or legacy JSON metadata is malformed.</exception>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">The extracted collection is not a readable supported SQLite database.</exception>
    /// <exception cref="InvalidOperationException">Malformed content creates a conflicting media registration or invalid object relationship.</exception>
    /// <exception cref="OutOfMemoryException">The process cannot allocate memory for an extracted media payload.</exception>
    /// <exception cref="OverflowException">The aggregate uncompressed archive length cannot be represented by <see cref="long"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The temporary workspace cannot be accessed.</exception>
    /// <exception cref="IOException">The source, a temporary file, or a temporary directory cannot be read, written, or removed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<AnkiPackage> ReadAsync(Stream source, AnkiPackageLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanSeek)
        {
            throw new ArgumentException("Package input must be seekable so archive limits can be checked before extraction.", nameof(source));
        }

        limits ??= AnkiPackageLimits.Default;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "AnkiIO-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            ValidateArchive(archive, limits);
            var collectionEntry = archive.GetEntry("collection.anki2") ?? throw new NotSupportedException("This package does not contain legacy collection.anki2. Modern collection.anki21b packages require a future adapter.");
            if (collectionEntry.Length > limits.MaximumCollectionBytes)
            {
                throw new AnkiPackageSecurityException($"Collection database exceeds the {limits.MaximumCollectionBytes} byte limit.");
            }

            var databasePath = Path.Combine(tempDirectory, "collection.anki2");
            await using (var input = collectionEntry.Open())
            await using (var output = new FileStream(databasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            var decks = await LegacyCollectionDatabase.ReadAsync(databasePath, cancellationToken).ConfigureAwait(false);
            var media = new AnkiMediaCollection();
            var diagnostics = new List<AnkiDiagnostic>
            {
                new(AnkiDiagnosticSeverity.Information, "PKG001", "Read legacy collection.anki2 package representation. Unknown SQLite columns and schema-18 protobuf metadata are not handled by this adapter."),
            };
            var mediaMapEntry = archive.GetEntry("media");
            if (mediaMapEntry is not null)
            {
                await using var mapStream = mediaMapEntry.Open();
                var map = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(mapStream, cancellationToken: cancellationToken).ConfigureAwait(false) ?? [];
                foreach (var pair in map.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    AnkiMediaCollection.ValidateFileName(pair.Value);
                    if (!pair.Key.All(char.IsAsciiDigit))
                    {
                        throw new AnkiPackageSecurityException($"Media entry key '{pair.Key}' is not numeric.");
                    }

                    var entry = archive.GetEntry(pair.Key) ?? throw new InvalidDataException($"Media map references missing entry '{pair.Key}'.");
                    if (entry.Length > int.MaxValue)
                    {
                        throw new AnkiPackageSecurityException($"Media '{pair.Value}' is too large for extracted in-memory ownership.");
                    }

                    await using var content = entry.Open();
                    using var memory = new MemoryStream((int)entry.Length);
                    await content.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
                    media.AddBytes(pair.Value, memory.ToArray());
                }
            }

            return new AnkiPackage(decks, media, diagnostics);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static void ValidateArchive(ZipArchive archive, AnkiPackageLimits limits)
    {
        if (archive.Entries.Count > limits.MaximumEntries)
        {
            throw new AnkiPackageSecurityException($"Archive contains {archive.Entries.Count} entries; limit is {limits.MaximumEntries}.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (!names.Add(entry.FullName))
            {
                throw new AnkiPackageSecurityException($"Duplicate archive path '{entry.FullName}'.");
            }

            if (entry.FullName.Length == 0 || Path.IsPathRooted(entry.FullName) || entry.FullName.Contains('/') || entry.FullName.Contains('\\') || entry.FullName is "." or "..")
            {
                throw new AnkiPackageSecurityException($"Unsafe archive path '{entry.FullName}'.");
            }

            var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000)
            {
                throw new AnkiPackageSecurityException($"Symbolic-link archive entry '{entry.FullName}' is not allowed.");
            }

            if (entry.Length > limits.MaximumEntryBytes)
            {
                throw new AnkiPackageSecurityException($"Entry '{entry.FullName}' exceeds the per-entry limit.");
            }

            total = checked(total + entry.Length);
            if (total > limits.MaximumTotalBytes)
            {
                throw new AnkiPackageSecurityException("Archive exceeds the total uncompressed-size limit.");
            }

            if (entry.CompressedLength > 0 && entry.Length / (double)entry.CompressedLength > limits.MaximumCompressionRatio)
            {
                throw new AnkiPackageSecurityException($"Entry '{entry.FullName}' exceeds the compression-ratio limit.");
            }
        }
    }
}
