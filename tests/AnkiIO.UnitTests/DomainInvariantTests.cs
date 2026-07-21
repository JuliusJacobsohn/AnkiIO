using Xunit;

namespace AnkiIO.UnitTests;

public sealed class DomainInvariantTests
{
    [Fact]
    public void CardSchedulingCannotBeNull()
    {
        Assert.Throws<ArgumentNullException>(() => new AnkiCard(1, 2, 3, 0, null!));

        var card = new AnkiCard(1, 2, 3, 0, AnkiScheduling.New);

        Assert.Throws<ArgumentNullException>(() => card.Scheduling = null!);
        Assert.Same(AnkiScheduling.New, card.Scheduling);
    }

    [Fact]
    public void ControlledCollectionViewsCannotMutateTheirOwners()
    {
        var deck = new AnkiDeck("Root");
        var subdecks = Assert.IsAssignableFrom<IList<AnkiDeck>>(deck.Subdecks);
        var notes = Assert.IsAssignableFrom<IList<AnkiNote>>(deck.Notes);
        var child = deck.AddSubdeck("Child");

        Assert.True(subdecks.IsReadOnly);
        Assert.True(notes.IsReadOnly);
        Assert.Same(child, Assert.Single(subdecks));
        Assert.Throws<NotSupportedException>(() => subdecks.Add(new AnkiDeck("Injected")));

        var noteType = AnkiNoteTypes.CreateBasic();
        var fields = Assert.IsAssignableFrom<IList<AnkiField>>(noteType.Fields);
        var templates = Assert.IsAssignableFrom<IList<AnkiCardTemplate>>(noteType.Templates);
        var note = deck.AddNote(noteType, new Dictionary<string, string>
        {
            ["Front"] = "Haus",
            ["Back"] = "house",
        });
        var noteFields = Assert.IsAssignableFrom<IDictionary<string, string>>(note.Fields);
        var cards = Assert.IsAssignableFrom<IList<AnkiCard>>(note.Cards);

        Assert.True(fields.IsReadOnly);
        Assert.True(templates.IsReadOnly);
        Assert.True(noteFields.IsReadOnly);
        Assert.True(cards.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => fields.Clear());
        Assert.Throws<NotSupportedException>(() => templates.Clear());
        Assert.Throws<NotSupportedException>(() => noteFields["Front"] = "mutated");
        Assert.Throws<NotSupportedException>(() => cards.Clear());
        Assert.Equal("Haus", note.Fields["Front"]);
        Assert.Single(note.Cards);

        var media = new AnkiMediaCollection();
        var registered = media.AddBytes("house.svg", [1, 2, 3]);
        var files = Assert.IsAssignableFrom<IList<AnkiMediaFile>>(media.Files);

        Assert.True(files.IsReadOnly);
        Assert.Same(registered, Assert.Single(files));
        Assert.Throws<NotSupportedException>(() => files.Clear());

        var compatibility = new Anki2605VersionAdapter();
        var schemas = Assert.IsAssignableFrom<ISet<int>>(compatibility.CollectionSchemas);
        var packageEntries = Assert.IsAssignableFrom<ISet<string>>(compatibility.PackageEntries);

        Assert.True(schemas.IsReadOnly);
        Assert.True(packageEntries.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => schemas.Add(99));
        Assert.Throws<NotSupportedException>(() => packageEntries.Clear());
    }

    [Fact]
    public void ChangingClozeTextReconcilesCardsAndPreservesMatchingState()
    {
        var deck = new AnkiDeck("Cloze");
        var note = deck.AddClozeNote("{{c1::one}} and {{c2::two}}");
        var removed = note.Cards.Single(card => card.TemplateOrdinal == 0);
        var preserved = note.Cards.Single(card => card.TemplateOrdinal == 1);
        var scheduling = new AnkiScheduling
        {
            Type = AnkiCardType.Review,
            Queue = AnkiCardQueue.Review,
            Due = 42,
            Interval = 7,
            EaseFactor = 2500,
            Repetitions = 3,
        };
        preserved.Scheduling = scheduling;
        preserved.Flag = 4;
        preserved.ReviewHistory.Add(new AnkiReviewLog(
            10,
            DateTimeOffset.UnixEpoch,
            3,
            7,
            2,
            2500,
            TimeSpan.FromSeconds(4),
            1));

        note.SetField("Text", "{{c2::updated}} and {{c3::new}}");

        Assert.Equal([1, 2], note.Cards.Select(card => card.TemplateOrdinal));
        Assert.Same(preserved, note.Cards[0]);
        Assert.Same(scheduling, note.Cards[0].Scheduling);
        Assert.Equal(4, note.Cards[0].Flag);
        Assert.Single(note.Cards[0].ReviewHistory);
        Assert.DoesNotContain(note.Cards, card => card.Id == removed.Id);
        Assert.Equal(deck.Id, note.Cards[1].DeckId);
        Assert.Same(AnkiScheduling.New, note.Cards[1].Scheduling);
    }

    [Fact]
    public void InvalidReplacementClozeTextLeavesFieldAndCardsUnchanged()
    {
        var deck = new AnkiDeck("Cloze");
        var note = deck.AddClozeNote("{{c1::valid}}");
        var originalCard = Assert.Single(note.Cards);

        Assert.Throws<ArgumentException>(() => note.SetField("Text", "{{c2147483648::overflow}}"));

        Assert.Equal("{{c1::valid}}", note.Fields["Text"]);
        Assert.Same(originalCard, Assert.Single(note.Cards));
    }

    [Fact]
    public void NoteTypeFreezesWhenUsedByANote()
    {
        var noteType = new AnkiNoteType("Vocabulary")
            .AddField("Word")
            .AddTemplate("Card", "{{Word}}", "{{Word}}");
        var note = new AnkiNote(noteType, new Dictionary<string, string> { ["Word"] = "Haus" });

        Assert.True(noteType.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => noteType.AddField("Meaning"));
        Assert.Throws<InvalidOperationException>(() => noteType.AddTemplate("Reverse", "{{Word}}", "{{Word}}"));
        Assert.Throws<InvalidOperationException>(() => noteType.Css = ".card { color: red; }");
        Assert.Single(note.NoteType.Fields);
        Assert.Single(note.NoteType.Templates);
    }

    [Fact]
    public void InvalidNoteConstructionDoesNotFreezeNoteType()
    {
        var noteType = new AnkiNoteType("Vocabulary")
            .AddField("Word")
            .AddTemplate("Card", "{{Word}}", "{{Word}}");

        Assert.Throws<ArgumentException>(() => new AnkiNote(
            noteType,
            new Dictionary<string, string> { ["Unknown"] = "value" }));

        Assert.False(noteType.IsFrozen);
        noteType.AddField("Meaning");
        Assert.Equal(2, noteType.Fields.Count);
    }

    [Fact]
    public void CardOrdinalsAndLegacyTagsRejectUnrepresentableValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnkiCard(1, 2, 3, -1, AnkiScheduling.New));

        var type = AnkiNoteTypes.CreateBasic();
        Assert.Throws<ArgumentException>(() => new AnkiNote(
            type,
            new Dictionary<string, string> { ["Front"] = "a", ["Back"] = "b" },
            ["two words"]));
        Assert.False(type.IsFrozen);

        var note = new AnkiNote(
            type,
            new Dictionary<string, string> { ["Front"] = "a", ["Back"] = "b" },
            ["language::german"]);

        Assert.Throws<ArgumentException>(() => note.AddTag("line\nbreak"));
        Assert.Equal(["language::german"], note.Tags);
    }
}
