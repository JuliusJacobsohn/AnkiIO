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
    public void NativeJsonRoundTripPreservesFieldAndBrowserTemplateMetadata()
    {
        var noteType = new AnkiNoteType("Arabic", id: 200)
            .AddConfiguredField(new AnkiField("Prompt", IsRightToLeft: true, IsSticky: true, Font: "Noto Sans Arabic", FontSize: 28))
            .AddField("Meaning")
            .AddConfiguredTemplate(new AnkiCardTemplate(
                "Recognition",
                "{{Prompt}}",
                "{{Meaning}}",
                BrowserQuestionFormat: "Question: {{Prompt}}",
                BrowserAnswerFormat: "Answer: {{Meaning}}"));
        var deck = new AnkiDeck("Language", 201);
        deck.AddNote(
            noteType,
            new Dictionary<string, string> { ["Prompt"] = "بيت", ["Meaning"] = "house" },
            id: 202);

        var restored = AnkiJsonSerializer.Deserialize(AnkiJsonSerializer.Serialize(deck));
        var restoredType = restored.Notes[0].NoteType;

        Assert.Equal(noteType.Fields, restoredType.Fields);
        Assert.Equal(noteType.Templates, restoredType.Templates);
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

    [Fact]
    public async Task AsyncJsonRoundTripLeavesStreamsOpen()
    {
        var deck = new AnkiDeck("Async");
        deck.AddNote(AnkiNoteTypes.CreateBasic(), new Dictionary<string, string> { ["Front"] = "a", ["Back"] = "b" });
        await using var stream = new MemoryStream();

        await AnkiJsonSerializer.WriteAsync(deck, stream);
        Assert.True(stream.CanWrite);
        stream.Position = 0;
        var restored = await AnkiJsonSerializer.ReadAsync(stream);

        Assert.True(stream.CanRead);
        Assert.Equal("Async", restored.Name);
    }
}
