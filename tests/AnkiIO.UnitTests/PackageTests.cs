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
        var note = child.AddNote(AnkiNoteTypes.CreateBasic(), new Dictionary<string, string>
        {
            ["Front"] = "<img src=\"house.png\">",
            ["Back"] = "Haus",
        }, ["tag"], "pkg-guid", 1002);
        note.Cards[0].Scheduling = new AnkiScheduling { Type = AnkiCardType.Review, Queue = AnkiCardQueue.Review, Due = 12, Interval = 8, EaseFactor = 2500, Repetitions = 2 };
        await using var stream = new MemoryStream();

        await AnkiPackageWriter.WriteAsync(deck, stream);
        stream.Position = 0;
        var package = await AnkiPackageReader.ReadAsync(stream);

        Assert.Equal("German", package.Decks[0].Subdecks[0].Name);
        Assert.Equal(8, package.Cards.Single().Scheduling.Interval);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([1, 3, 3, 7])).ToLowerInvariant(), package.Media.Files.Single().Sha256);
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
}
