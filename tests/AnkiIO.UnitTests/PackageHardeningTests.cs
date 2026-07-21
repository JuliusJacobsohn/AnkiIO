using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AnkiIO.UnitTests;

public sealed class PackageHardeningTests
{
    [Theory]
    [InlineData(nameof(AnkiPackageLimits.MaximumEntries))]
    [InlineData(nameof(AnkiPackageLimits.MaximumEntryBytes))]
    [InlineData(nameof(AnkiPackageLimits.MaximumTotalBytes))]
    [InlineData(nameof(AnkiPackageLimits.MaximumCollectionBytes))]
    public void LimitsRejectNonPositiveCountsAndSizes(string propertyName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = propertyName switch
            {
                nameof(AnkiPackageLimits.MaximumEntries) => new AnkiPackageLimits { MaximumEntries = 0 },
                nameof(AnkiPackageLimits.MaximumEntryBytes) => new AnkiPackageLimits { MaximumEntryBytes = 0 },
                nameof(AnkiPackageLimits.MaximumTotalBytes) => new AnkiPackageLimits { MaximumTotalBytes = 0 },
                nameof(AnkiPackageLimits.MaximumCollectionBytes) => new AnkiPackageLimits { MaximumCollectionBytes = 0 },
                _ => throw new InvalidOperationException(),
            });

        Assert.Equal(propertyName, exception.ParamName);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void LimitsRejectNonFiniteOrNonPositiveCompressionRatios(double value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = AnkiPackageLimits.Default with { MaximumCompressionRatio = value });

        Assert.Equal(nameof(AnkiPackageLimits.MaximumCompressionRatio), exception.ParamName);
    }

    [Fact]
    public async Task PathWriterDoesNotReplaceDestinationWhenValidationFails()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.FullName, "deck.apkg");
        byte[] original = [7, 6, 5, 4];
        await File.WriteAllBytesAsync(destination, original);
        var deck = CreateDeck();
        deck.Notes.Single().Cards.Single().Flag = 8;

        await Assert.ThrowsAsync<AnkiValidationException>(() => AnkiPackageWriter.WriteAsync(deck, destination));

        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(directory.FullName, ".AnkiIO-write-*.tmp"));
    }

    [Fact]
    public async Task PathWriterKeepsDestinationAndCleansTemporaryFileAfterLateFailure()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.FullName, "deck.apkg");
        var mediaPath = Path.Combine(directory.FullName, "sound.mp3");
        byte[] original = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(destination, original);
        await File.WriteAllBytesAsync(mediaPath, [10, 20, 30]);
        var deck = CreateDeck();
        await deck.Media.AddFileAsync(mediaPath);
        await File.WriteAllBytesAsync(mediaPath, [30, 20, 10]);

        await Assert.ThrowsAsync<InvalidDataException>(() => AnkiPackageWriter.WriteAsync(deck, destination));

        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(directory.FullName, ".AnkiIO-write-*.tmp"));
    }

    [Fact]
    public async Task PackageOverloadRetainsReadMediaAndIncludesNewDeckMedia()
    {
        var deck = CreateDeck();
        deck.Media.AddBytes("original.png", [1, 3, 3, 7]);
        await using var first = new MemoryStream();
        await AnkiPackageWriter.WriteAsync(deck, first);
        first.Position = 0;
        var package = await AnkiPackageReader.ReadAsync(first);
        Assert.Empty(package.Decks.Single().Media.Files);
        package.Decks.Single().Media.AddBytes("new.png", [2, 4, 6, 8]);

        await using var rewritten = new MemoryStream();
        await AnkiPackageWriter.WriteAsync(package, rewritten);
        rewritten.Position = 0;
        var restored = await AnkiPackageReader.ReadAsync(rewritten);

        Assert.Equal(["new.png", "original.png"], restored.Media.Files.Select(file => file.FileName));
        var originalMedia = restored.Media.Files.Single(file => file.FileName == "original.png");
        await using var content = await originalMedia.OpenReadAsync();
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal([1, 3, 3, 7], copy.ToArray());
    }

    [Fact]
    public async Task ReaderRejectsNonSeekableInputAndWriterSupportsNonSeekableOutput()
    {
        await using var unreadableArchive = new MemoryStream();
        await using (var nonSeekable = new GZipStream(unreadableArchive, CompressionMode.Decompress, leaveOpen: true))
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => AnkiPackageReader.ReadAsync(nonSeekable));
            Assert.Equal("source", exception.ParamName);
        }

        var deck = CreateDeck();
        await using var output = new MemoryStream();
        await using (var nonSeekable = new NonSeekableWriteStream(output))
        {
            Assert.False(nonSeekable.CanSeek);
            await AnkiPackageWriter.WriteAsync(deck, nonSeekable);
        }

        output.Position = 0;
        var package = await AnkiPackageReader.ReadAsync(output);
        Assert.Equal("Deck", package.Decks.Single().Name);
    }

    [Fact]
    public async Task ReaderRejectsDuplicateArchiveNames()
    {
        await using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("collection.anki2");
            archive.CreateEntry("collection.anki2");
        }

        stream.Position = 0;
        var exception = await Assert.ThrowsAsync<AnkiPackageSecurityException>(() => AnkiPackageReader.ReadAsync(stream));
        Assert.Contains("Duplicate archive path", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("entry")]
    [InlineData("total")]
    [InlineData("collection")]
    public async Task ReaderEnforcesConfiguredCountAndSizeLimits(string limit)
    {
        var entries = limit is "count" or "total"
            ? new[] { ("collection.anki2", new byte[] { 1, 2 }), ("media", new byte[] { 3, 4 }) }
            : new[] { ("collection.anki2", new byte[] { 1, 2, 3 }) };
        await using var stream = CreateArchive(entries);
        var limits = limit switch
        {
            "count" => new AnkiPackageLimits { MaximumEntries = 1 },
            "entry" => new AnkiPackageLimits { MaximumEntryBytes = 2 },
            "total" => new AnkiPackageLimits { MaximumTotalBytes = 3 },
            "collection" => new AnkiPackageLimits { MaximumCollectionBytes = 2 },
            _ => throw new InvalidOperationException(),
        };

        await Assert.ThrowsAsync<AnkiPackageSecurityException>(() => AnkiPackageReader.ReadAsync(stream, limits));
    }

    [Theory]
    [InlineData("{\"not-numeric\":\"sound.mp3\"}")]
    [InlineData("{\"\":\"sound.mp3\"}")]
    [InlineData("{\"0\":\"../sound.mp3\"}")]
    [InlineData("{\"0\":null}")]
    public async Task ReaderRejectsUnsafeMediaMapKeysAndNames(string mediaMap)
    {
        await using var stream = await CreatePackageWithMediaMapAsync(mediaMap);
        await Assert.ThrowsAsync<AnkiPackageSecurityException>(() => AnkiPackageReader.ReadAsync(stream));
    }

    [Fact]
    public async Task ReaderRejectsMediaMapThatReferencesMissingEntry()
    {
        await using var stream = await CreatePackageWithMediaMapAsync("{\"0\":\"sound.mp3\"}");
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => AnkiPackageReader.ReadAsync(stream));
        Assert.Contains("missing entry", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReaderRejectsMalformedMediaMapJson()
    {
        await using var stream = await CreatePackageWithMediaMapAsync("{");
        await Assert.ThrowsAsync<JsonException>(() => AnkiPackageReader.ReadAsync(stream));
    }

    private static AnkiDeck CreateDeck()
    {
        var deck = new AnkiDeck("Deck");
        deck.AddBasicNote("front", "back");
        return deck;
    }

    private static MemoryStream CreateArchive(IEnumerable<(string Name, byte[] Content)> entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var target = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
                target.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static async Task<MemoryStream> CreatePackageWithMediaMapAsync(string mediaMap)
    {
        var stream = new MemoryStream();
        await AnkiPackageWriter.WriteAsync(CreateDeck(), stream);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("media")!.Delete();
            var entry = archive.CreateEntry("media", CompressionLevel.NoCompression);
            await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteAsync(mediaMap);
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => FullName = Directory.CreateTempSubdirectory("AnkiIO-hardening-").FullName;

        public string FullName { get; }

        public void Dispose()
        {
            if (Directory.Exists(FullName))
            {
                Directory.Delete(FullName, recursive: true);
            }
        }
    }

    private sealed class NonSeekableWriteStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    }
}
