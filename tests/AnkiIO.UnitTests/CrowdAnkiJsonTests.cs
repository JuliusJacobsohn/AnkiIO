using Xunit;

namespace AnkiIO.UnitTests;

public sealed class CrowdAnkiJsonTests
{
    [Fact]
    public void InspiredFormatRoundTripPreservesSupportedConcepts()
    {
        var deck = new AnkiDeck("Languages", 10) { Description = "Synthetic" };
        var child = deck.AddSubdeck("German", 11);
        child.AddBasicNote("Haus", "house", ["german"], "guid-1", 12);

        var json = CrowdAnkiJson.Export(deck);
        var imported = CrowdAnkiJson.Import(json);

        Assert.Contains("\"__type__\": \"Deck\"", json, StringComparison.Ordinal);
        Assert.Equal("German", imported.Deck.Subdecks[0].Name);
        Assert.Equal("house", imported.Deck.Subdecks[0].Notes[0].Fields["Back"]);
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "CROWD001");
        Assert.Same(imported.Deck.Subdecks[0].Notes[0].NoteType, imported.Deck.AddBasicNote("Katze", "cat").NoteType);
    }

    [Fact]
    public void InspiredFormatRoundTripPreservesSupportedEditorAndBrowserFormats()
    {
        var type = new AnkiNoteType("Vocabulary", id: 20)
            .AddConfiguredField(new AnkiField("Prompt", IsRightToLeft: true, IsSticky: true, Font: "Noto Sans Arabic", FontSize: 26))
            .AddField("Answer")
            .AddConfiguredTemplate(new AnkiCardTemplate(
                "Card",
                "{{Prompt}}",
                "{{Answer}}",
                BrowserQuestionFormat: "Q: {{Prompt}}",
                BrowserAnswerFormat: "A: {{Answer}}"));
        var deck = new AnkiDeck("Languages", 21);
        deck.AddNote(type, new Dictionary<string, string> { ["Prompt"] = "بيت", ["Answer"] = "house" }, guid: "arabic-guid", id: 22);

        var imported = CrowdAnkiJson.Import(CrowdAnkiJson.Export(deck));
        var restoredType = imported.Deck.Notes.Single().NoteType;

        Assert.Equal(type.Fields, restoredType.Fields);
        Assert.Equal(type.Templates, restoredType.Templates);
    }
}
