using System.IO.Compression;
using System.Text.Json;

namespace AnkiIO;

/// <summary>Reads untrusted legacy-representation APKG files with configurable archive limits.</summary>
public static class AnkiPackageReader
{
    /// <summary>Reads a package file without modifying it.</summary>
    public static async Task<AnkiPackage> ReadAsync(string path, AnkiPackageLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadAsync(stream, limits, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a package from a seekable caller-owned stream and leaves it open.</summary>
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
