using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AnkiIO;

/// <summary>Writes guarded legacy-representation APKG files accepted by the Anki 25.02.7 importer.</summary>
public static class AnkiPackageWriter
{
    /// <summary>Writes a deck hierarchy to a new package file without touching an Anki profile.</summary>
    public static async Task WriteAsync(AnkiDeck deck, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await WriteAsync(deck, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a package to a caller-owned stream and leaves it open.</summary>
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

