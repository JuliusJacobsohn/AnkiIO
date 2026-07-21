using System.Text.Json.Nodes;
using Xunit;

namespace AnkiIO.UnitTests;

public sealed class SchedulingValidationTests
{
    public static IEnumerable<object[]> SchedulingPairs()
    {
        var types = new[]
        {
            AnkiCardType.New,
            AnkiCardType.Learning,
            AnkiCardType.Review,
            AnkiCardType.Relearning,
        };
        var queues = new[]
        {
            AnkiCardQueue.Suspended,
            AnkiCardQueue.SiblingBuried,
            AnkiCardQueue.SchedulerBuried,
            AnkiCardQueue.New,
            AnkiCardQueue.Learning,
            AnkiCardQueue.Review,
            AnkiCardQueue.DayLearning,
            AnkiCardQueue.Preview,
        };

        foreach (var type in types)
        {
            foreach (var queue in queues)
            {
                var specialQueue = queue is AnkiCardQueue.Suspended
                    or AnkiCardQueue.SiblingBuried
                    or AnkiCardQueue.SchedulerBuried
                    or AnkiCardQueue.Preview;
                var activePair = type switch
                {
                    AnkiCardType.New => queue == AnkiCardQueue.New,
                    AnkiCardType.Learning => queue is AnkiCardQueue.Learning or AnkiCardQueue.DayLearning,
                    AnkiCardType.Review => queue == AnkiCardQueue.Review,
                    AnkiCardType.Relearning => queue is AnkiCardQueue.Learning or AnkiCardQueue.DayLearning,
                    _ => false,
                };
                yield return [type, queue, specialQueue || activePair];
            }
        }
    }

    [Theory]
    [MemberData(nameof(SchedulingPairs))]
    public void ValidatorAcceptsExactlyTheSupportedQueueTypeMatrix(AnkiCardType type, AnkiCardQueue queue, bool expectedValid)
    {
        var deck = CreateDeckWithOneCard();
        deck.Notes[0].Cards[0].Scheduling = new AnkiScheduling { Type = type, Queue = queue };

        var result = AnkiValidator.Validate(deck);

        if (expectedValid)
        {
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI030");
        }
        else
        {
            var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == "ANKI030");
            Assert.Equal(deck.Notes[0].Cards[0].Id, diagnostic.CardId);
        }
    }

    [Theory]
    [InlineData(99, (int)AnkiCardQueue.Suspended)]
    [InlineData((int)AnkiCardType.Review, 99)]
    public void ValidatorRejectsUnknownSchedulingEnumValuesEvenForSpecialQueues(int type, int queue)
    {
        var deck = CreateDeckWithOneCard();
        deck.Notes[0].Cards[0].Scheduling = new AnkiScheduling
        {
            Type = (AnkiCardType)type,
            Queue = (AnkiCardQueue)queue,
        };

        var result = AnkiValidator.Validate(deck);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI030");
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    public void ValidatorEnforcesThreeBitCardFlagRange(int flag, bool expectedError)
    {
        var deck = CreateDeckWithOneCard();
        deck.Notes[0].Cards[0].Flag = flag;

        var result = AnkiValidator.Validate(deck);

        Assert.Equal(expectedError, result.Diagnostics.Any(diagnostic => diagnostic.Code == "ANKI032"));
    }

    [Theory]
    [InlineData(-1, 0, true)]
    [InlineData(0, -1, true)]
    [InlineData(-1, -1, true)]
    [InlineData(0, 0, false)]
    [InlineData(5, 2, false)]
    public void ValidatorRejectsNegativeReviewCounters(int repetitions, int lapses, bool expectedError)
    {
        var deck = CreateDeckWithOneCard();
        deck.Notes[0].Cards[0].Scheduling = new AnkiScheduling
        {
            Type = AnkiCardType.Review,
            Queue = AnkiCardQueue.Review,
            Repetitions = repetitions,
            Lapses = lapses,
        };

        var result = AnkiValidator.Validate(deck);

        Assert.Equal(expectedError, result.Diagnostics.Any(diagnostic => diagnostic.Code == "ANKI033"));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void ValidatorRejectsEveryReviewDerivedCounterOnNewCards(int interval, int repetitions, int lapses)
    {
        var deck = CreateDeckWithOneCard();
        deck.Notes[0].Cards[0].Scheduling = new AnkiScheduling
        {
            Type = AnkiCardType.New,
            Queue = AnkiCardQueue.New,
            Interval = interval,
            Repetitions = repetitions,
            Lapses = lapses,
        };

        var result = AnkiValidator.Validate(deck);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI031");
    }

    [Fact]
    public void ValidatorReportsDuplicateDeckAndNoteIdentifiersWithContext()
    {
        var root = new AnkiDeck("Root", 700);
        root.AddSubdeck("Child", 700);
        root.AddBasicNote("first", "answer", guid: "first-guid", id: 701);
        root.AddBasicNote("second", "answer", guid: "second-guid", id: 701);

        var result = AnkiValidator.Validate(root);
        var deckDiagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI001");
        var noteDiagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI002");

        Assert.Equal(700, deckDiagnostic.DeckId);
        Assert.Equal(701, noteDiagnostic.NoteId);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidatorSharesIdentityAndNoteTypeChecksAcrossRootHierarchies()
    {
        var firstType = new AnkiNoteType("First type", id: 750)
            .AddField("Front")
            .AddTemplate("Card", "{{Front}}", "{{Front}}");
        var secondType = new AnkiNoteType("Different type", id: 750)
            .AddField("Value")
            .AddTemplate("Card", "{{Value}}", "{{Value}}");
        var first = new AnkiDeck("First", id: 751);
        var second = new AnkiDeck("Second", id: 751);
        first.AddNote(firstType, new Dictionary<string, string> { ["Front"] = "one" }, id: 752);
        second.AddNote(secondType, new Dictionary<string, string> { ["Value"] = "two" }, id: 753);

        var result = AnkiValidator.Validate([first, second]);

        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI001");
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI004");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void MultiRootValidatorRejectsMissingRootsBeforeEnumeration()
    {
        Assert.Throws<ArgumentNullException>(() => AnkiValidator.Validate((IEnumerable<AnkiDeck>)null!));
        Assert.Throws<ArgumentException>(() => AnkiValidator.Validate(Array.Empty<AnkiDeck>()));
        Assert.Throws<ArgumentException>(() => AnkiValidator.Validate(new AnkiDeck[] { null! }));
    }

    [Fact]
    public void NativeImportReportsDuplicateCardIdentifiersAsStructuredValidation()
    {
        var deck = new AnkiDeck("Cards", 800);
        deck.AddBasicNote("first", "answer", guid: "first-guid", id: 801);
        deck.AddBasicNote("second", "answer", guid: "second-guid", id: 802);
        var json = JsonNode.Parse(AnkiJsonSerializer.Serialize(deck))!.AsObject();
        var notes = json["deck"]!["notes"]!.AsArray();
        var firstCardId = notes[0]!["cards"]![0]!["id"]!.GetValue<long>();
        notes[1]!["cards"]![0]!["id"] = firstCardId;

        var exception = Assert.Throws<AnkiValidationException>(() => AnkiJsonSerializer.Deserialize(json.ToJsonString()));
        var diagnostic = Assert.Single(exception.ValidationResult.Diagnostics, value => value.Code == "ANKI003");

        Assert.Equal(firstCardId, diagnostic.CardId);
    }

    [Fact]
    public void ValidatorReportsMissingNoteTypeFieldsAndTemplates()
    {
        var noFields = new AnkiNoteType("No fields", id: 900)
            .AddTemplate("Literal", "question", "answer");
        var noTemplates = new AnkiNoteType("No templates", id: 901)
            .AddField("Front");
        var deck = new AnkiDeck("Structures", 902);
        deck.AddNote(noFields, new Dictionary<string, string>(), guid: "no-fields", id: 903);
        deck.AddNote(noTemplates, new Dictionary<string, string> { ["Front"] = "value" }, guid: "no-templates", id: 904);

        var result = AnkiValidator.Validate(deck);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI010" && diagnostic.NoteId == 903);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ANKI011" && diagnostic.NoteId == 904);
    }

    [Fact]
    public void ValidatorUnderstandsSupportedTemplateModifiersAndReportsUnknownFields()
    {
        var validType = new AnkiNoteType("Valid template", id: 1000)
            .AddField("Front")
            .AddTemplate("Card", "{{#Front}}{{text:Front}}{{/Front}}", "{{FrontSide}}");
        var invalidType = new AnkiNoteType("Invalid template", id: 1001)
            .AddField("Front")
            .AddTemplate("Card", "{{type:Missing}}", "{{Front}}");
        var deck = new AnkiDeck("Templates", 1002);
        deck.AddNote(validType, new Dictionary<string, string> { ["Front"] = "valid" }, guid: "valid-template", id: 1003);
        deck.AddNote(invalidType, new Dictionary<string, string> { ["Front"] = "invalid" }, guid: "invalid-template", id: 1004);

        var result = AnkiValidator.Validate(deck);
        var diagnostic = Assert.Single(result.Diagnostics, value => value.Code == "ANKI020");

        Assert.Equal(1004, diagnostic.NoteId);
        Assert.Equal("Missing", diagnostic.FieldName);
    }

    [Fact]
    public void ValidationResultDiagnosticsAreAnImmutableSnapshot()
    {
        var type = new AnkiNoteType("Invalid", id: 1100)
            .AddField("Front")
            .AddTemplate("Card", "{{Missing}}", "{{Front}}");
        var deck = new AnkiDeck("Snapshot", 1101);
        deck.AddNote(type, new Dictionary<string, string> { ["Front"] = "value" }, guid: "snapshot-guid", id: 1102);
        var result = AnkiValidator.Validate(deck);
        var diagnostics = Assert.IsAssignableFrom<IList<AnkiDiagnostic>>(result.Diagnostics);
        var count = diagnostics.Count;

        Assert.True(diagnostics.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => diagnostics.Clear());
        Assert.Equal(count, result.Diagnostics.Count);
        Assert.False(result.IsValid);
    }

    private static AnkiDeck CreateDeckWithOneCard()
    {
        var deck = new AnkiDeck("Scheduling");
        deck.AddBasicNote("front", "back");
        return deck;
    }
}
