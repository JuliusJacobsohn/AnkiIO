using System.Text.Json;
using Xunit;

namespace AnkiIO.UnitTests;

public sealed class JsonTests
{
    [Fact]
    public void NativeJsonRoundTripPreservesSemanticDataAndIsDeterministic()
    {
        var root = new AnkiDeck("Languages", 100) { Description = "A hierarchy" };
        root.Metadata["owner"] = "synthetic";
        var child = root.AddSubdeck("German", 101);
        var note = child.AddNote(AnkiNoteTypes.CreateBasicAndReversed(), new Dictionary<string, string>
        {
            ["Front"] = "Haus",
            ["Back"] = "house",
        }, ["vocabulary", "german"], "stable-guid", 102);
        note.Cards[0].Scheduling = new AnkiScheduling
        {
            Type = AnkiCardType.Review,
            Queue = AnkiCardQueue.Review,
            Due = 42,
            Interval = 10,
            EaseFactor = 2500,
            Repetitions = 3,
        };

        var first = AnkiJsonSerializer.Serialize(root);
        var restored = AnkiJsonSerializer.Deserialize(first);
        var second = AnkiJsonSerializer.Serialize(restored);

        Assert.Equal(first, second);
        Assert.Equal("house", restored.Subdecks[0].Notes[0].Fields["Back"]);
        Assert.Equal(10, restored.Subdecks[0].Notes[0].Cards[0].Scheduling.Interval);
    }

    [Fact]
    public void UnknownDeckPropertyIsPreserved()
    {
        var deck = new AnkiDeck("Deck", 1);
        deck.AddNote(AnkiNoteTypes.CreateBasic(), new Dictionary<string, string> { ["Front"] = "a", ["Back"] = "b" }, id: 2);
        var json = AnkiJsonSerializer.Serialize(deck).Replace("\"description\": \"\",", "\"description\": \"\",\n    \"futureValue\": { \"enabled\": true },", StringComparison.Ordinal);

        var restored = AnkiJsonSerializer.Deserialize(json);

        Assert.True(restored.UnknownData["futureValue"].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void UnsupportedVersionFailsActionably()
    {
        var exception = Assert.Throws<JsonException>(() => AnkiJsonSerializer.Deserialize("{\"formatVersion\":99,\"noteTypes\":[],\"deck\":{}}"));
        Assert.Contains("Unsupported", exception.Message, StringComparison.Ordinal);
    }
}
