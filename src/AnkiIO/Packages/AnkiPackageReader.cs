using System.IO.Compression;
using System.Text.Json;

namespace AnkiIO;

/// <summary>Reads guarded legacy-compatible <c>.apkg</c> archives into AnkiIO's in-memory package model.</summary>
/// <remarks>
/// <para>
/// This reader treats the ZIP directory, entry names and sizes, media map, and SQLite database as untrusted. It validates
/// <see cref="AnkiPackageLimits"/> and archive metadata before extracting the collection database or copying media. Limits
/// mitigate common archive-exhaustion attacks but are not a sandbox; see <see cref="AnkiPackageLimits"/> for the threat
/// model and choose smaller bounds for network uploads.
/// </para>
/// <para>
/// The supported input is a ZIP archive containing <c>collection.anki2</c> and legacy JSON model/deck metadata. Collection
/// schema values 11 and 18 are accepted only when that metadata is still JSON. Modern native entries such as
/// <c>collection.anki21</c>, <c>collection.anki21b</c>, schema-18 protobuf metadata, and <c>meta</c> are not decoded. Anki
/// 26.05 was verified to import the legacy representation written by AnkiIO; this reader does not claim general support for
/// every package exported by that version.
/// </para>
/// <para>
/// Decks, notes, front/back templates, cards, supported scheduling fields, low three-bit card flags, descriptions, field
/// editor settings, browser-only template formats, and mapped media are reconstructed. Review-log rows, deck configuration,
/// graves, unknown SQLite columns, note/card auxiliary columns, and unsupported model metadata are not preserved.
/// Consult <see cref="AnkiPackage.Diagnostics"/> after reading.
/// </para>
/// <para>
/// Media payloads are eagerly copied into <see cref="AnkiPackage.Media"/> and are not attached to individual decks. The
/// returned package owns those copies and no archive handles, but may retain memory proportional to allowed media size.
/// Separate calls may run concurrently when they use separate streams; returned graphs are mutable and not thread-safe.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var limits = AnkiPackageLimits.Default with { MaximumTotalBytes = 128L * 1024 * 1024 };
/// var package = await AnkiPackageReader.ReadAsync("deck.apkg", limits);
/// foreach (var diagnostic in package.Diagnostics)
/// {
///     Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
/// }
/// </code>
/// </example>
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
    /// The source file is opened with read access and file sharing for other readers, and is closed before the task
    /// completes. The file is never modified and no Anki profile is opened. The collection database is extracted into an
    /// isolated temporary directory that is removed on success, cancellation, or failure. Media is copied eagerly into the
    /// returned package. Cancellation is observed by asynchronous file, database, and media operations; synchronous ZIP
    /// metadata validation is not individually cancelable.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank or has invalid path syntax.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> contains a non-positive count or byte bound, or a non-finite or non-positive compression ratio.</exception>
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
        limits ??= AnkiPackageLimits.Default;
        limits.Validate();
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
    /// Position <paramref name="source"/> at the beginning of the complete ZIP archive before calling. The caller retains
    /// ownership and the stream remains open on success or failure. Its final position is unspecified and the original
    /// position is not restored. Do not read, write, seek, or dispose it concurrently. Seeking is required because the ZIP
    /// central directory must be inspected before extraction. The collection database is copied to an isolated temporary
    /// directory and removed afterward; media is copied eagerly into the returned package. Cancellation is observed by
    /// asynchronous stream, database, and media operations, not every synchronous metadata check.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not readable and seekable.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> contains a non-positive count or byte bound, or a non-finite or non-positive compression ratio.</exception>
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
        if (!source.CanRead || !source.CanSeek)
        {
            throw new ArgumentException("Package input must be readable and seekable so archive limits can be checked before extraction.", nameof(source));
        }

        limits ??= AnkiPackageLimits.Default;
        limits.Validate();
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
                var map = await ReadMediaMapAsync(mapStream, cancellationToken).ConfigureAwait(false);
                foreach (var pair in map.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    try
                    {
                        AnkiMediaCollection.ValidateFileName(pair.Value);
                    }
                    catch (ArgumentException)
                    {
                        throw new AnkiPackageSecurityException($"Media map entry '{pair.Key}' contains an unsafe filename.");
                    }

                    if (pair.Key.Length == 0 || !pair.Key.All(char.IsAsciiDigit))
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

    private static async Task<IReadOnlyDictionary<string, string>> ReadMediaMapAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The media map must be a JSON object whose property names are numeric archive entries and whose values are filenames.");
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"Media map entry '{property.Name}' must contain a JSON string filename.");
            }

            if (!map.TryAdd(property.Name, property.Value.GetString()!))
            {
                throw new JsonException($"The media map contains duplicate entry key '{property.Name}'.");
            }
        }

        return map;
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

            if (entry.Length > 0 && entry.CompressedLength == 0)
            {
                throw new AnkiPackageSecurityException($"Non-empty entry '{entry.FullName}' reports zero compressed bytes.");
            }

            if (entry.CompressedLength > 0 && entry.Length / (double)entry.CompressedLength > limits.MaximumCompressionRatio)
            {
                throw new AnkiPackageSecurityException($"Entry '{entry.FullName}' exceeds the compression-ratio limit.");
            }
        }
    }
}
