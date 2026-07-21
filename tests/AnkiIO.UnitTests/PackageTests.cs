using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;

namespace AnkiIO.UnitTests;

public sealed class PackageTests
{
    [Fact]
    public async Task LegacyPackageRoundTripPreservesHierarchySchedulingAndMedia()
    {
        var deck = new AnkiDeck("Languages", 1000);
        deck.Media.AddBytes("house.png", [1, 3, 3, 7]);
        var child = deck.AddSubdeck("German", 1001);
        var note = child.AddBasicNote("<img src=\"house.png\">", "Haus", ["tag"], "pkg-guid", 1002);
        note.Cards[0].Scheduling = new AnkiScheduling { Type = AnkiCardType.Review, Queue = AnkiCardQueue.Review, Due = 12, Interval = 8, EaseFactor = 2500, Repetitions = 2 };
        await using var stream = new MemoryStream();

        await AnkiPackageWriter.WriteAsync(deck, stream);
        stream.Position = 0;
        var package = await AnkiPackageReader.ReadAsync(stream);

        Assert.Equal("German", package.Decks[0].Subdecks[0].Name);
        Assert.Equal(8, package.Cards.Single().Scheduling.Interval);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([1, 3, 3, 7])).ToLowerInvariant(), package.Media.Files.Single().Sha256);
        Assert.Same(package.Notes.Single().NoteType, package.Decks[0].AddBasicNote("Katze", "cat").NoteType);
    }

    [Fact]
    public async Task LegacyPackageRoundTripPreservesSupportedEditorAndBrowserFormats()
    {
        var type = new AnkiNoteType("Arabic", id: 1100)
            .AddConfiguredField(new AnkiField("Prompt", IsRightToLeft: true, IsSticky: true, Font: "Noto Sans Arabic", FontSize: 28))
            .AddField("Meaning")
            .AddConfiguredTemplate(new AnkiCardTemplate(
                "Recognition",
                "{{Prompt}}",
                "{{Meaning}}",
                BrowserQuestionFormat: "Q: {{Prompt}}",
                BrowserAnswerFormat: "A: {{Meaning}}"));
        var deck = new AnkiDeck("Language", 1101);
        deck.AddNote(type, new Dictionary<string, string> { ["Prompt"] = "بيت", ["Meaning"] = "house" }, id: 1102);
        await using var stream = new MemoryStream();

        await AnkiPackageWriter.WriteAsync(deck, stream);
        stream.Position = 0;
        var package = await AnkiPackageReader.ReadAsync(stream);
        var restoredType = package.Notes.Single().NoteType;

        Assert.Equal(type.Fields, restoredType.Fields);
        Assert.Equal(type.Templates, restoredType.Templates);
    }

    [Fact]
    public async Task ReaderRejectsTraversalEntryBeforeExtraction()
    {
        await using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("../collection.anki2");
        }

        stream.Position = 0;
        await Assert.ThrowsAsync<AnkiPackageSecurityException>(() => AnkiPackageReader.ReadAsync(stream));
    }

    [Fact]
    public async Task StreamOverloadsLeaveCallerStreamsOpen()
    {
        var deck = new AnkiDeck("Deck");
        deck.AddNote(AnkiNoteTypes.CreateBasic(), new Dictionary<string, string> { ["Front"] = "a", ["Back"] = "b" });
        await using var stream = new MemoryStream();

        await AnkiPackageWriter.WriteAsync(deck, stream);
        Assert.True(stream.CanWrite);
        stream.Position = 0;
        await AnkiPackageReader.ReadAsync(stream);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ReaderEnforcesEntryCountAndCompressionRatio()
    {
        await using var entries = new MemoryStream();
        using (var archive = new ZipArchive(entries, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("collection.anki2");
            archive.CreateEntry("media");
        }

        entries.Position = 0;
        await Assert.ThrowsAsync<AnkiPackageSecurityException>(() => AnkiPackageReader.ReadAsync(entries, new AnkiPackageLimits { MaximumEntries = 1 }));

        await using var compressed = new MemoryStream();
        using (var archive = new ZipArchive(compressed, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var target = archive.CreateEntry("collection.anki2", CompressionLevel.Optimal).Open();
            await target.WriteAsync(new byte[100_000]);
        }

        compressed.Position = 0;
        await Assert.ThrowsAsync<AnkiPackageSecurityException>(() => AnkiPackageReader.ReadAsync(compressed, new AnkiPackageLimits { MaximumCompressionRatio = 2 }));
    }

    [Fact]
    public async Task ReaderRejectsSymlinksAndModernOnlyPackages()
    {
        await using var symlink = new MemoryStream();
        using (var archive = new ZipArchive(symlink, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("collection.anki2").ExternalAttributes = 0xA000 << 16;
        }

        symlink.Position = 0;
        await Assert.ThrowsAsync<AnkiPackageSecurityException>(() => AnkiPackageReader.ReadAsync(symlink));

        await using var modern = new MemoryStream();
        using (var archive = new ZipArchive(modern, ZipArchiveMode.Create, leaveOpen: true)) archive.CreateEntry("collection.anki21b");
        modern.Position = 0;
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => AnkiPackageReader.ReadAsync(modern));
        Assert.Contains("collection.anki21b", exception.Message, StringComparison.Ordinal);
    }
}
