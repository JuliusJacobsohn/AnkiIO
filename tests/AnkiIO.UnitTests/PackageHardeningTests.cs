using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
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
    public async Task ReaderRejectsUnreadableSeekableInput()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.FullName, "write-only.apkg");
        await using var source = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Assert.True(source.CanSeek);
        Assert.False(source.CanRead);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => AnkiPackageReader.ReadAsync(source));

        Assert.Equal("source", exception.ParamName);
    }

    [Fact]
    public async Task WriterRejectsReadOnlyDestinationBeforeWriting()
    {
        byte[] original = [4, 3, 2, 1];
        await using var destination = new MemoryStream(original, writable: false);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => AnkiPackageWriter.WriteAsync(CreateDeck(), destination));

        Assert.Equal("destination", exception.ParamName);
        Assert.Equal(original, destination.ToArray());
        Assert.Equal(0, destination.Position);
    }

    [Fact]
    public async Task ConflictingHierarchyMediaIsRejectedBeforeStreamMutation()
    {
        var deck = CreateDeck();
        deck.Media.AddBytes("shared.png", [1]);
        deck.AddSubdeck("Child").Media.AddBytes("shared.png", [2]);
        await using var destination = new MemoryStream();
        await destination.WriteAsync(new byte[] { 9, 8, 7 });
        destination.Position = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => AnkiPackageWriter.WriteAsync(deck, destination));

        Assert.Equal([9, 8, 7], destination.ToArray());
        Assert.Equal(1, destination.Position);
    }

    [Fact]
    public async Task PackageCollectionsAreReadOnlySnapshotsAndMultipleRootsRoundTrip()
    {
        var roots = new List<AnkiDeck> { CreateDeck("First"), CreateDeck("Second") };
        var diagnostics = new List<AnkiDiagnostic>
        {
            new(AnkiDiagnosticSeverity.Information, "TEST001", "Fixture diagnostic."),
        };
        var package = CreatePackage(roots, diagnostics);
        roots.Clear();
        diagnostics.Clear();

        Assert.Equal(2, package.Decks.Count);
        Assert.Single(package.Diagnostics);
        var deckList = Assert.IsAssignableFrom<IList<AnkiDeck>>(package.Decks);
        var diagnosticList = Assert.IsAssignableFrom<IList<AnkiDiagnostic>>(package.Diagnostics);
        Assert.True(deckList.IsReadOnly);
        Assert.True(diagnosticList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => deckList.Add(CreateDeck("Third")));
        Assert.Throws<NotSupportedException>(() => diagnosticList.Clear());

        await using var output = new MemoryStream();
        await AnkiPackageWriter.WriteAsync(package, output);
        output.Position = 0;
        var restored = await AnkiPackageReader.ReadAsync(output);
        Assert.Equal(["First", "Second"], restored.Decks.Select(deck => deck.Name).Order(StringComparer.Ordinal));
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

    [Fact]
    public async Task ReaderRejectsNonEmptyEntryReportingZeroCompressedBytes()
    {
        await using var valid = CreateArchive([("collection.anki2", new byte[] { 1 })]);
        var bytes = valid.ToArray();
        var centralDirectory = FindSignature(bytes, 0x02014b50u);
        Assert.True(centralDirectory >= 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(centralDirectory + 20, sizeof(uint)), 0);
        await using var malformed = new MemoryStream(bytes, writable: false);

        var exception = await Assert.ThrowsAsync<AnkiPackageSecurityException>(() => AnkiPackageReader.ReadAsync(malformed));

        Assert.Contains("zero compressed bytes", exception.Message, StringComparison.Ordinal);
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
        await Assert.ThrowsAnyAsync<JsonException>(() => AnkiPackageReader.ReadAsync(stream));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"0\":null}")]
    [InlineData("{\"0\":123}")]
    [InlineData("{\"0\":\"first.png\",\"0\":\"second.png\"}")]
    public async Task ReaderRejectsStructurallyInvalidMediaMaps(string mediaMap)
    {
        await using var stream = await CreatePackageWithMediaMapAsync(mediaMap);
        await Assert.ThrowsAnyAsync<JsonException>(() => AnkiPackageReader.ReadAsync(stream));
    }

    private static AnkiDeck CreateDeck(string name = "Deck")
    {
        var deck = new AnkiDeck(name);
        deck.AddBasicNote("front", "back");
        return deck;
    }

    private static AnkiPackage CreatePackage(IReadOnlyList<AnkiDeck> roots, IReadOnlyList<AnkiDiagnostic> diagnostics)
    {
        var constructor = typeof(AnkiPackage).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(IReadOnlyList<AnkiDeck>), typeof(AnkiMediaCollection), typeof(IReadOnlyList<AnkiDiagnostic>)],
            modifiers: null)!;
        return (AnkiPackage)constructor.Invoke([roots, new AnkiMediaCollection(), diagnostics]);
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

    private static int FindSignature(ReadOnlySpan<byte> bytes, uint signature)
    {
        for (var index = 0; index <= bytes.Length - sizeof(uint); index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[index..]) == signature)
            {
                return index;
            }
        }

        return -1;
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
