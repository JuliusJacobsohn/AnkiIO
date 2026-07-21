using Xunit;

namespace AnkiIO.UnitTests;

public sealed class CrowdAnkiJsonTests
{
    [Fact]
    public void InspiredFormatRoundTripPreservesSupportedConcepts()
    {
        var deck = new AnkiDeck("Languages", 10) { Description = "Synthetic" };
        var child = deck.AddSubdeck("German", 11);
        child.AddNote(AnkiNoteTypes.CreateBasic(), new Dictionary<string, string>
        {
            ["Front"] = "Haus",
            ["Back"] = "house",
        }, ["german"], "guid-1", 12);

        var json = CrowdAnkiJson.Export(deck);
        var imported = CrowdAnkiJson.Import(json);

        Assert.Contains("\"__type__\": \"Deck\"", json, StringComparison.Ordinal);
        Assert.Equal("German", imported.Deck.Subdecks[0].Name);
        Assert.Equal("house", imported.Deck.Subdecks[0].Notes[0].Fields["Back"]);
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "CROWD001");
    }
}
