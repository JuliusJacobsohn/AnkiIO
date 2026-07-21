using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace AnkiIO.UnitTests;

public sealed class SerializationHardeningTests
{
    [Fact]
    public void NativeJsonDeclaresStableOnePointZeroGenerator()
    {
        var deck = new AnkiDeck("Generator", 100);
        deck.AddBasicNote("front", "back", guid: "generator-guid", id: 101);
        using (var extension = JsonDocument.Parse("{\"enabled\":true}"))
        {
            deck.UnknownData["extension"] = extension.RootElement.Clone();
        }

        using var document = JsonDocument.Parse(AnkiJsonSerializer.Serialize(deck));

        Assert.Equal(AnkiJsonSerializer.CurrentFormatVersion, document.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("AnkiIO/1.0", document.RootElement.GetProperty("generator").GetString());
        Assert.True(document.RootElement.GetProperty("deck").GetProperty("extension").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task NativeJsonRoundTripPreservesCompleteSchedulingFlagsAndOrderedReviewHistory()
    {
        var deck = new AnkiDeck("Scheduling", 200);
        var note = deck.AddBasicNote("front", "back", ["one", "two"], "scheduling-guid", 201);
        var card = Assert.Single(note.Cards);
        var scheduling = new AnkiScheduling
        {
            Type = AnkiCardType.Review,
            Queue = AnkiCardQueue.Review,
            Due = 42,
            Interval = 14,
            EaseFactor = 2650,
            Repetitions = 9,
            Lapses = 2,
            RemainingSteps = 1002,
            OriginalDue = 17,
            OriginalDeckId = 199,
            CustomData = "{\"seed\":7}",
        };
        card.Scheduling = scheduling;
        card.Flag = 7;
        var later = new AnkiReviewLog(
            20,
            new DateTimeOffset(2026, 7, 22, 12, 30, 0, TimeSpan.FromHours(2)),
            4,
            14,
            7,
            2650,
            TimeSpan.FromMilliseconds(1450),
            1);
        var earlier = new AnkiReviewLog(
            10,
            new DateTimeOffset(2026, 7, 21, 10, 15, 0, TimeSpan.FromHours(-3)),
            3,
            7,
            3,
            2500,
            TimeSpan.FromMilliseconds(975),
            0);
        card.ReviewHistory.Add(later);
        card.ReviewHistory.Add(earlier);
        await using var stream = new MemoryStream();

        await AnkiJsonSerializer.WriteAsync(deck, stream);
        stream.Position = 0;
        var restored = await AnkiJsonSerializer.ReadAsync(stream);
        var restoredCard = Assert.Single(Assert.Single(restored.Notes).Cards);

        Assert.Equal(scheduling, restoredCard.Scheduling);
        Assert.Equal(7, restoredCard.Flag);
        Assert.Equal([earlier, later], restoredCard.ReviewHistory);
        Assert.Equal(["one", "two"], restored.Notes[0].Tags);
    }

    [Theory]
    [InlineData("noteTypes")]
    [InlineData("noteTypeEntry")]
    [InlineData("noteTypeFields")]
    [InlineData("noteTypeFieldEntry")]
    [InlineData("noteTypeTemplates")]
    [InlineData("noteTypeTemplateEntry")]
    [InlineData("metadata")]
    [InlineData("metadataValue")]
    [InlineData("notes")]
    [InlineData("noteEntry")]
    [InlineData("noteFields")]
    [InlineData("noteFieldEntry")]
    [InlineData("tags")]
    [InlineData("tagEntry")]
    [InlineData("cards")]
    [InlineData("cardEntry")]
    [InlineData("scheduling")]
    [InlineData("reviewHistory")]
    [InlineData("reviewEntry")]
    [InlineData("subdecks")]
    [InlineData("subdeckEntry")]
    public void NativeJsonRejectsNullStructuralShapesWithJsonException(string target)
    {
        var root = CreateNativeDocumentNode();
        SetNativeTargetToNull(root, target);

        var exception = Assert.Throws<JsonException>(() => AnkiJsonSerializer.Deserialize(root.ToJsonString()));

        Assert.DoesNotContain("Object reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeJsonRejectsMissingRootMissingModelAndWrongFieldCount()
    {
        var missingRoot = CreateNativeDocumentNode();
        missingRoot.Remove("deck");
        Assert.Contains("root deck", Assert.Throws<JsonException>(() => AnkiJsonSerializer.Deserialize(missingRoot.ToJsonString())).Message, StringComparison.OrdinalIgnoreCase);

        var missingModel = CreateNativeDocumentNode();
        NativeNote(missingModel)["noteTypeId"] = long.MaxValue;
        Assert.Contains("missing note type", Assert.Throws<JsonException>(() => AnkiJsonSerializer.Deserialize(missingModel.ToJsonString())).Message, StringComparison.OrdinalIgnoreCase);

        var wrongFieldCount = CreateNativeDocumentNode();
        NativeNote(wrongFieldCount)["fields"]!.AsArray().RemoveAt(0);
        Assert.Contains("requires", Assert.Throws<JsonException>(() => AnkiJsonSerializer.Deserialize(wrongFieldCount.ToJsonString())).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NativeJsonRejectsLiteralNullDocumentsSynchronouslyAndAsynchronously()
    {
        Assert.Contains("empty", Assert.Throws<JsonException>(() => AnkiJsonSerializer.Deserialize("null")).Message, StringComparison.OrdinalIgnoreCase);

        await using var source = Utf8Stream("null");
        var exception = await Assert.ThrowsAsync<JsonException>(() => AnkiJsonSerializer.ReadAsync(source));
        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AsyncNativeReaderUsesTheSameVersionAndShapeValidation()
    {
        await using var unsupported = Utf8Stream("{\"formatVersion\":99,\"noteTypes\":[],\"deck\":{}}");
        var versionException = await Assert.ThrowsAsync<JsonException>(() => AnkiJsonSerializer.ReadAsync(unsupported));
        Assert.Contains("Unsupported", versionException.Message, StringComparison.Ordinal);

        var nullTypes = CreateNativeDocumentNode();
        nullTypes["noteTypes"] = null;
        await using var malformed = Utf8Stream(nullTypes.ToJsonString());
        var shapeException = await Assert.ThrowsAsync<JsonException>(() => AnkiJsonSerializer.ReadAsync(malformed));
        Assert.Contains("noteTypes", shapeException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeJsonAsyncOperationsObservePreCancellation()
    {
        var deck = new AnkiDeck("Cancelled", 300);
        deck.AddBasicNote("front", "back", guid: "cancel-guid", id: 301);
        var cancellationToken = new CancellationToken(canceled: true);
        await using var destination = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnkiJsonSerializer.WriteAsync(deck, destination, cancellationToken));

        await using var source = Utf8Stream(AnkiJsonSerializer.Serialize(deck));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnkiJsonSerializer.ReadAsync(source, cancellationToken));
    }

    [Fact]
    public void NativeAndCrowdExportsExposeStructuredValidationFailures()
    {
        var type = new AnkiNoteType("Invalid", id: 400)
            .AddField("Front")
            .AddTemplate("Card", "{{Missing}}", "{{Front}}");
        var deck = new AnkiDeck("Invalid", 401);
        deck.AddNote(type, new Dictionary<string, string> { ["Front"] = "value" }, guid: "invalid-guid", id: 402);

        var native = Assert.Throws<AnkiValidationException>(() => AnkiJsonSerializer.Serialize(deck));
        var crowd = Assert.Throws<AnkiValidationException>(() => CrowdAnkiJson.Export(deck));

        Assert.Contains(native.ValidationResult.Diagnostics, diagnostic => diagnostic.Code == "ANKI020");
        Assert.Contains(crowd.ValidationResult.Diagnostics, diagnostic => diagnostic.Code == "ANKI020");
        Assert.Contains("1 error", native.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationExceptionRejectsAMissingDiagnosticSnapshot()
    {
        Assert.Throws<ArgumentNullException>(() => new AnkiValidationException(null!));
    }

    [Fact]
    public void CrowdAnkiImportAppliesDocumentedDefaultsIncludingClozeModels()
    {
        const string json = """
            {
              "name": "Defaults",
              "note_models": [
                {
                  "crowdanki_uuid": "model-defaults",
                  "name": "Cloze defaults",
                  "type": 1,
                  "flds": [{ "name": "Text" }],
                  "tmpls": [{ "name": "Card" }]
                }
              ],
              "notes": [
                {
                  "guid": "default-guid",
                  "note_model_uuid": "model-defaults",
                  "fields": ["{{c1::answer}}"],
                  "tags": [null, "", "kept"]
                }
              ],
              "media_files": null,
              "children": null
            }
            """;

        var result = CrowdAnkiJson.Import(json);
        var note = Assert.Single(result.Deck.Notes);
        var field = Assert.Single(note.NoteType.Fields);
        var template = Assert.Single(note.NoteType.Templates);

        Assert.Equal(AnkiNoteTypeKind.Cloze, note.NoteType.Kind);
        Assert.False(field.IsRightToLeft);
        Assert.False(field.IsSticky);
        Assert.Equal("Arial", field.Font);
        Assert.Equal(20, field.FontSize);
        Assert.Equal(string.Empty, template.QuestionFormat);
        Assert.Equal(string.Empty, template.AnswerFormat);
        Assert.Null(template.BrowserQuestionFormat);
        Assert.Null(template.BrowserAnswerFormat);
        Assert.Equal(["kept"], note.Tags);
        Assert.Single(note.Cards);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CROWD001");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CROWD002");
        var diagnostics = Assert.IsAssignableFrom<IList<AnkiDiagnostic>>(result.Diagnostics);
        Assert.True(diagnostics.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => diagnostics.Clear());
    }

    [Fact]
    public void CrowdAnkiMediaNamesRoundTripAsWarningsAtTheirDeckLocations()
    {
        var root = new AnkiDeck("Root", 500);
        root.Media.AddBytes("root.svg", [1]);
        var child = root.AddSubdeck("Child", 501);
        child.Media.AddBytes("child.mp3", [2]);

        var json = CrowdAnkiJson.Export(root);
        var imported = CrowdAnkiJson.Import(json);
        var warnings = imported.Diagnostics.Where(diagnostic => diagnostic.Code == "CROWD002").ToArray();

        Assert.Contains("root.svg", json, StringComparison.Ordinal);
        Assert.Contains("child.mp3", json, StringComparison.Ordinal);
        Assert.Equal(2, warnings.Length);
        Assert.Equal(["$", "$.children"], warnings.Select(warning => warning.Location));
        Assert.Empty(imported.Deck.Media.Files);
        Assert.Empty(imported.Deck.Subdecks[0].Media.Files);
    }

    [Fact]
    public void CrowdAnkiRejectsUnknownModelsAndMismatchedFieldCounts()
    {
        var unknownModel = CreateCrowdDocumentNode();
        CrowdNote(unknownModel)["note_model_uuid"] = "absent";
        Assert.Contains("unknown", Assert.Throws<JsonException>(() => CrowdAnkiJson.Import(unknownModel.ToJsonString())).Message, StringComparison.OrdinalIgnoreCase);

        var fieldMismatch = CreateCrowdDocumentNode();
        CrowdNote(fieldMismatch)["fields"] = new JsonArray();
        Assert.Contains("requires", Assert.Throws<JsonException>(() => CrowdAnkiJson.Import(fieldMismatch.ToJsonString())).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrowdAnkiNullFieldValuesUseTheDocumentedEmptyStringDefault()
    {
        var document = CreateCrowdDocumentNode();
        CrowdNote(document)["fields"]!.AsArray()[0] = null;

        var imported = CrowdAnkiJson.Import(document.ToJsonString());

        Assert.Equal(string.Empty, Assert.Single(imported.Deck.Notes).Fields["Front"]);
    }

    [Theory]
    [InlineData("rootArray")]
    [InlineData("noteModelsScalar")]
    [InlineData("noteModelNull")]
    [InlineData("fieldNull")]
    [InlineData("templateNull")]
    [InlineData("noteNull")]
    [InlineData("childNull")]
    [InlineData("requiredStringNumber")]
    [InlineData("requiredStringBlank")]
    [InlineData("optionalScalarType")]
    [InlineData("noteFieldsScalar")]
    [InlineData("fieldValueObject")]
    public void CrowdAnkiMalformedShapesConsistentlyThrowJsonException(string target)
    {
        var json = CreateMalformedCrowdJson(target);

        var exception = Assert.Throws<JsonException>(() => CrowdAnkiJson.Import(json));

        Assert.DoesNotContain("Object reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject CreateNativeDocumentNode()
    {
        var deck = new AnkiDeck("Root", 600);
        deck.Metadata["owner"] = "tests";
        deck.AddSubdeck("Child", 601);
        var note = deck.AddBasicNote("front", "back", ["tag"], "native-guid", 602);
        note.Cards[0].ReviewHistory.Add(new AnkiReviewLog(
            603,
            DateTimeOffset.UnixEpoch,
            3,
            2,
            1,
            2500,
            TimeSpan.FromMilliseconds(500),
            0));
        return JsonNode.Parse(AnkiJsonSerializer.Serialize(deck))!.AsObject();
    }

    private static void SetNativeTargetToNull(JsonObject root, string target)
    {
        var noteTypes = root["noteTypes"]!.AsArray();
        var noteType = noteTypes[0]!.AsObject();
        var deck = root["deck"]!.AsObject();
        var notes = deck["notes"]!.AsArray();
        var note = notes[0]!.AsObject();
        var cards = note["cards"]!.AsArray();
        var card = cards[0]!.AsObject();

        switch (target)
        {
            case "noteTypes": root["noteTypes"] = null; break;
            case "noteTypeEntry": noteTypes[0] = null; break;
            case "noteTypeFields": noteType["fields"] = null; break;
            case "noteTypeFieldEntry": noteType["fields"]!.AsArray()[0] = null; break;
            case "noteTypeTemplates": noteType["templates"] = null; break;
            case "noteTypeTemplateEntry": noteType["templates"]!.AsArray()[0] = null; break;
            case "metadata": deck["metadata"] = null; break;
            case "metadataValue": deck["metadata"]!["owner"] = null; break;
            case "notes": deck["notes"] = null; break;
            case "noteEntry": notes[0] = null; break;
            case "noteFields": note["fields"] = null; break;
            case "noteFieldEntry": note["fields"]!.AsArray()[0] = null; break;
            case "tags": note["tags"] = null; break;
            case "tagEntry": note["tags"]!.AsArray()[0] = null; break;
            case "cards": note["cards"] = null; break;
            case "cardEntry": cards[0] = null; break;
            case "scheduling": card["scheduling"] = null; break;
            case "reviewHistory": card["reviewHistory"] = null; break;
            case "reviewEntry": card["reviewHistory"]!.AsArray()[0] = null; break;
            case "subdecks": deck["subdecks"] = null; break;
            case "subdeckEntry": deck["subdecks"]!.AsArray()[0] = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown native JSON target.");
        }
    }

    private static JsonObject NativeNote(JsonObject root) => root["deck"]!["notes"]![0]!.AsObject();

    private static JsonObject CreateCrowdDocumentNode() => JsonNode.Parse("""
        {
          "__type__": "Deck",
          "crowdanki_uuid": "root-uuid",
          "name": "Root",
          "note_models": [
            {
              "crowdanki_uuid": "model-uuid",
              "name": "Model",
              "type": 0,
              "css": "",
              "flds": [{ "name": "Front", "rtl": false, "sticky": false, "font": "Arial", "size": 20 }],
              "tmpls": [{ "name": "Card", "qfmt": "{{Front}}", "afmt": "{{Front}}" }]
            }
          ],
          "notes": [
            {
              "guid": "crowd-guid",
              "note_model_uuid": "model-uuid",
              "fields": ["front"],
              "tags": ["tag"]
            }
          ],
          "media_files": [],
          "children": [{ "crowdanki_uuid": "child-uuid", "name": "Child" }]
        }
        """)!.AsObject();

    private static JsonObject CrowdNote(JsonObject root) => root["notes"]![0]!.AsObject();

    private static string CreateMalformedCrowdJson(string target)
    {
        if (target == "rootArray")
        {
            return "[]";
        }

        var root = CreateCrowdDocumentNode();
        var noteModels = root["note_models"]!.AsArray();
        var model = noteModels[0]!.AsObject();
        var note = CrowdNote(root);
        switch (target)
        {
            case "noteModelsScalar": root["note_models"] = 3; break;
            case "noteModelNull": noteModels[0] = null; break;
            case "fieldNull": model["flds"]!.AsArray()[0] = null; break;
            case "templateNull": model["tmpls"]!.AsArray()[0] = null; break;
            case "noteNull": root["notes"]!.AsArray()[0] = null; break;
            case "childNull": root["children"]!.AsArray()[0] = null; break;
            case "requiredStringNumber": model["name"] = 17; break;
            case "requiredStringBlank": model["name"] = "   "; break;
            case "optionalScalarType": model["type"] = "cloze"; break;
            case "noteFieldsScalar": note["fields"] = 4; break;
            case "fieldValueObject": note["fields"]!.AsArray()[0] = new JsonObject(); break;
            default: throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown CrowdAnki target.");
        }

        return root.ToJsonString();
    }

    private static MemoryStream Utf8Stream(string value) => new(Encoding.UTF8.GetBytes(value));
}
