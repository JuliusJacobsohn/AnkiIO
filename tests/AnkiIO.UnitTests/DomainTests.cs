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

    [Fact]
    public void BasicHelperCreatesOneCardAndPreservesCommonNoteProperties()
    {
        var deck = new AnkiDeck("Vocabulary");

        var first = deck.AddBasicNote(
            "Haus",
            "house",
            ["german", "noun", "noun"],
            guid: "stable-guid",
            id: 101);
        var second = deck.AddBasicNote("Katze", "cat");

        Assert.Equal(101, first.Id);
        Assert.Equal("stable-guid", first.Guid);
        Assert.Equal("Haus", first.Fields["Front"]);
        Assert.Equal("house", first.Fields["Back"]);
        Assert.Equal(["german", "noun"], first.Tags);
        Assert.Single(first.Cards);
        Assert.Same(first.NoteType, second.NoteType);
        Assert.Equal("Basic", first.NoteType.Name);
    }

    [Fact]
    public void ReversedHelperSharesItsNoteTypeAcrossDeckHierarchy()
    {
        var root = new AnkiDeck("Languages");
        var german = root.AddSubdeck("German");
        var french = root.AddSubdeck("French");

        var first = german.AddBasicAndReversedNote("gehen", "to go");
        var second = french.AddBasicAndReversedNote("aller", "to go");

        Assert.Equal(2, first.Cards.Count);
        Assert.Equal([0, 1], first.Cards.Select(card => card.TemplateOrdinal));
        Assert.Same(first.NoteType, second.NoteType);
        Assert.Equal("Basic (and reversed card)", first.NoteType.Name);
    }

    [Fact]
    public void ExplicitConventionalTypesSeedEveryHierarchyHelper()
    {
        var root = new AnkiDeck("Languages");
        var imported = root.AddSubdeck("Imported");
        var basic = AnkiNoteTypes.CreateBasic();
        var reversed = AnkiNoteTypes.CreateBasicAndReversed();
        var cloze = AnkiNoteTypes.CreateCloze();
        imported.AddNote(basic, new Dictionary<string, string> { ["Front"] = "a", ["Back"] = "b" });
        imported.AddNote(reversed, new Dictionary<string, string> { ["Front"] = "c", ["Back"] = "d" });
        imported.AddNote(cloze, new Dictionary<string, string> { ["Text"] = "{{c1::e}}", ["Extra"] = string.Empty });

        Assert.Same(basic, root.AddBasicNote("f", "g").NoteType);
        Assert.Same(reversed, root.AddBasicAndReversedNote("h", "i").NoteType);
        Assert.Same(cloze, root.AddClozeNote("{{c1::j}}").NoteType);

        var customized = AnkiNoteTypes.CreateBasic();
        customized.Css += " ";
        var separate = new AnkiDeck("Separate");
        separate.AddNote(customized, new Dictionary<string, string> { ["Front"] = "x", ["Back"] = "y" });
        Assert.NotSame(customized, separate.AddBasicNote("z", "w").NoteType);
    }

    [Fact]
    public void NativeImportPropagatesObservedConventionalTypesToNewSiblings()
    {
        var root = new AnkiDeck("Languages", 501);
        var imported = root.AddSubdeck("Imported", 502);
        imported.AddBasicNote("a", "b", id: 503);
        imported.AddBasicAndReversedNote("c", "d", id: 504);
        imported.AddClozeNote("{{c1::e}}", id: 505);

        var restored = AnkiJsonSerializer.Deserialize(AnkiJsonSerializer.Serialize(root));
        var restoredTypes = restored.Subdecks[0].Notes
            .Select(note => note.NoteType)
            .ToDictionary(noteType => noteType.Name, StringComparer.Ordinal);
        var sibling = restored.AddSubdeck("New", 506);

        Assert.Same(restoredTypes["Basic"], sibling.AddBasicNote("f", "g", id: 507).NoteType);
        Assert.Same(restoredTypes["Basic (and reversed card)"], sibling.AddBasicAndReversedNote("h", "i", id: 508).NoteType);
        Assert.Same(restoredTypes["Cloze"], sibling.AddClozeNote("{{c1::j}}", id: 509).NoteType);
    }

    [Fact]
    public async Task PackageImportObservesConventionalTypesAddedAfterHierarchyConstruction()
    {
        var root = new AnkiDeck("Languages", 520);
        root.AddSubdeck("Imported", 521).AddBasicNote("a", "b", id: 522);
        await using var stream = new MemoryStream();
        await AnkiPackageWriter.WriteAsync(root, stream);
        stream.Position = 0;

        var package = await AnkiPackageReader.ReadAsync(stream);
        var restored = Assert.Single(package.Decks);
        var restoredType = Assert.Single(Assert.Single(restored.Subdecks).Notes).NoteType;

        Assert.Same(restoredType, restored.AddBasicNote("c", "d", id: 523).NoteType);
    }

    [Fact]
    public void ClozeHelpersCreateMarkupFieldsAndDistinctCards()
    {
        var deck = new AnkiDeck("Geography");
        var text = $"{AnkiCloze.Wrap("Berlin", hint: "city")} is in {AnkiCloze.Wrap("Germany", 2)}; {AnkiCloze.Wrap("Brandenburg Gate")} is there.";

        var note = deck.AddClozeNote(text, "European geography", ["capitals"]);

        Assert.Equal("{{c1::Berlin::city}} is in {{c2::Germany}}; {{c1::Brandenburg Gate}} is there.", note.Fields["Text"]);
        Assert.Equal("European geography", note.Fields["Extra"]);
        Assert.Equal(["capitals"], note.Tags);
        Assert.Equal([0, 1], note.Cards.Select(card => card.TemplateOrdinal));
    }

    [Fact]
    public void ClozeConvenienceMethodsRejectContentThatCannotProduceValidMarkup()
    {
        var deck = new AnkiDeck("Invalid cloze");

        Assert.Throws<ArgumentException>(() => deck.AddClozeNote("No deletion here."));
        Assert.Throws<ArgumentException>(() => deck.AddClozeNote("{{c0::zero is not a card}}"));
        Assert.Empty(deck.Notes);
        Assert.Throws<ArgumentException>(() => AnkiCloze.Wrap("contains::delimiter"));
        Assert.Throws<ArgumentException>(() => AnkiCloze.Wrap("contains {{ nested marker"));
        Assert.Throws<ArgumentException>(() => AnkiCloze.Wrap("answer", hint: "bad}}hint"));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnkiCloze.Wrap("answer", 0));
    }

    [Theory]
    [InlineData("{{c1::unterminated")]
    [InlineData("{{c1::}}")]
    [InlineData("{{c1::outer {{c2::inner}}}}")]
    [InlineData("{{c1::answer::hint::extra}}")]
    [InlineData("{{c0::zero}}")]
    [InlineData("{{c01::leading zero}}")]
    [InlineData("{{c2147483648::overflow}}")]
    public void ClozeHelperRejectsMalformedSimpleMarkup(string text)
    {
        var deck = new AnkiDeck("Invalid cloze");

        var exception = Assert.Throws<ArgumentException>(() => deck.AddClozeNote(text));

        Assert.Equal("text", exception.ParamName);
        Assert.Empty(deck.Notes);
    }

    [Fact]
    public void ClozeHelperRejectsMalformedMarkerBeforeValidDeletion()
    {
        var deck = new AnkiDeck("Invalid cloze");

        var exception = Assert.Throws<ArgumentException>(() =>
            deck.AddClozeNote("{{cx::malformed}} followed by {{c1::valid}}"));

        Assert.Equal("text", exception.ParamName);
        Assert.Contains("positive numeric index", exception.Message, StringComparison.Ordinal);
        Assert.Empty(deck.Notes);
    }

    [Fact]
    public void LowLevelClozeGenerationRejectsUnrepresentableIndexesWithoutOverflowing()
    {
        var deck = new AnkiDeck("Advanced cloze");

        var exception = Assert.Throws<ArgumentException>(() => deck.AddNote(
                AnkiNoteTypes.CreateCloze(),
                new Dictionary<string, string>
                {
                    ["Text"] = "{{c2147483648::too large}} and {{c1::valid}}",
                    ["Extra"] = string.Empty,
                }));

        Assert.Equal("fields", exception.ParamName);
        Assert.Contains("2147483648", exception.Message, StringComparison.Ordinal);
        Assert.Empty(deck.Notes);
    }

    [Fact]
    public void ValidationRejectsOnlyConflictingDefinitionsForTheSameNoteTypeId()
    {
        const long sharedId = 700;
        var first = new AnkiNoteType("Shared", id: sharedId)
            .AddField("Front")
            .AddTemplate("Card", "{{Front}}", "{{Front}}");
        var equivalent = new AnkiNoteType("Shared", id: sharedId)
            .AddField("Front")
            .AddTemplate("Card", "{{Front}}", "{{Front}}");
        var conflicting = new AnkiNoteType("Shared", id: sharedId)
            .AddField("Front")
            .AddTemplate("Card", "{{Front}}", "changed");
        var deck = new AnkiDeck("Definitions");
        deck.AddNote(first, new Dictionary<string, string> { ["Front"] = "one" });
        deck.AddNote(first, new Dictionary<string, string> { ["Front"] = "same instance" });
        deck.AddNote(equivalent, new Dictionary<string, string> { ["Front"] = "equivalent" });

        Assert.DoesNotContain(AnkiValidator.Validate(deck).Diagnostics, diagnostic => diagnostic.Code == "ANKI004");

        deck.AddNote(conflicting, new Dictionary<string, string> { ["Front"] = "conflict" });
        var result = AnkiValidator.Validate(deck);

        var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == "ANKI004");
        Assert.Equal(AnkiDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(result.IsValid);
    }
}
