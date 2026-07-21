using Xunit;

namespace AnkiIO.UnitTests;

public sealed class DomainTests
{
    [Fact]
    public void NestedDecksAndReversedTypeGenerateExpectedCards()
    {
        var root = new AnkiDeck("Languages");
        var verbs = root.AddSubdeck("German").AddSubdeck("Verbs");
        var note = verbs.AddNote(AnkiNoteTypes.CreateBasicAndReversed(), new Dictionary<string, string>
        {
            ["Front"] = "gehen",
            ["Back"] = "to go",
        });

        Assert.Equal(3, root.Traverse().Count());
        Assert.Equal(2, note.Cards.Count);
        Assert.All(note.Cards, card => Assert.Equal(AnkiCardQueue.New, card.Scheduling.Queue));
    }

    [Fact]
    public void ClozeCreatesOneCardPerDistinctPositiveIndex()
    {
        var deck = new AnkiDeck("Geography");
        var note = deck.AddNote(AnkiNoteTypes.CreateCloze(), new Dictionary<string, string>
        {
            ["Text"] = "{{c2::Germany}} has {{c1::Berlin}} and another {{c1::clue}}.",
            ["Extra"] = string.Empty,
        });

        Assert.Equal([0, 1], note.Cards.Select(card => card.TemplateOrdinal));
    }

    [Fact]
    public void ValidationReportsInvalidSchedulingAndTemplateField()
    {
        var type = new AnkiNoteType("Broken")
            .AddField("Front")
            .AddTemplate("Card", "{{Missing}}", "{{Front}}");
        var deck = new AnkiDeck("Deck");
        var note = deck.AddNote(type, new Dictionary<string, string> { ["Front"] = "value" });
        note.Cards[0].Scheduling = new AnkiScheduling { Type = AnkiCardType.Review, Queue = AnkiCardQueue.New };

        var result = AnkiValidator.Validate(deck);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI020");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI030");
    }

    [Fact]
    public void MediaRejectsTraversalAndFilenameCollisions()
    {
        var media = new AnkiMediaCollection();
        media.AddBytes("image.png", [1, 2, 3]);

        Assert.Throws<ArgumentException>(() => media.AddBytes("../image.png", [1]));
        Assert.Throws<InvalidOperationException>(() => media.AddBytes("image.png", [3, 2, 1]));
    }

    [Fact]
    public void StableValueIdentifierIsRepeatableAndScoped()
    {
        Assert.Equal(AnkiId.FromStableValue("note", "abc"), AnkiId.FromStableValue("note", "abc"));
        Assert.NotEqual(AnkiId.FromStableValue("note", "abc"), AnkiId.FromStableValue("card", "abc"));
    }

    [Fact]
    public void ControlledMutationRejectsDuplicatesAndSupportsRemoval()
    {
        var type = AnkiNoteTypes.CreateBasic();
        Assert.Throws<ArgumentException>(() => type.AddField("front"));
        Assert.Throws<ArgumentException>(() => type.AddTemplate("card 1", "x", "y"));
        var deck = new AnkiDeck("Root");
        deck.AddSubdeck("Child");
        Assert.Throws<ArgumentException>(() => deck.AddSubdeck("child"));
        var note = deck.AddNote(type, new Dictionary<string, string> { ["Front"] = "a", ["Back"] = "b" }, ["one"]);
        note.AddTag("two");
        Assert.True(note.RemoveTag("one"));
        Assert.True(deck.RemoveNote(note));
    }
}
