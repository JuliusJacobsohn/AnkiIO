using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnkiIO;

/// <summary>Imports and exports a conservative, independently implemented subset of CrowdAnki-style JSON.</summary>
/// <remarks>
/// This adapter follows publicly observable CrowdAnki concepts such as deck and note-model UUIDs, nested children, ordered
/// fields, templates, notes, tags, and media filenames. It does not claim full CrowdAnki compatibility or byte-for-byte
/// equivalence. Scheduling, card IDs, review history, card flags, deck configuration contents, custom deck metadata, unknown
/// extension data, media bytes, and arbitrary model attributes outside the supported field/template shape are omitted or
/// defaulted. Prefer
/// <see cref="AnkiJsonSerializer"/> or an Anki package when those values must survive a round trip.
/// </remarks>
public static class CrowdAnkiJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Exports a validated deck hierarchy using the supported CrowdAnki-style JSON concepts.</summary>
    /// <param name="root">The root deck whose descendants, note models, notes, tags, and media filenames will be exported.</param>
    /// <returns>
    /// Indented UTF-16 JSON with CrowdAnki-style type markers and a trailing line-feed character.
    /// </returns>
    /// <remarks>
    /// Stable CrowdAnki UUID strings are derived from AnkiIO's numeric deck and note-type IDs. Notes retain their Anki GUIDs.
    /// The root description is exported, but descendant descriptions are omitted to match the supported shape. Media entries
    /// contain filenames only; callers must copy the corresponding payloads beside the JSON document themselves. The export
    /// emits an empty <c>deck_configurations</c> array and does not preserve scheduling, generated-card identities, review
    /// history, flags, custom metadata, unknown data, or arbitrary model attributes. Field editor settings and
    /// browser-specific template formats are represented. The method does not mutate
    /// <paramref name="root"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="AnkiValidationException">
    /// The hierarchy contains one or more error-severity validation diagnostics and cannot be exported safely.
    /// </exception>
    public static string Export(AnkiDeck root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var validation = AnkiValidator.Validate(root);
        if (!validation.IsValid)
        {
            throw new AnkiValidationException(validation);
        }

        var types = root.Traverse().SelectMany(deck => deck.Notes).Select(note => note.NoteType).GroupBy(type => type.Id).Select(group => group.First()).OrderBy(type => type.Id).ToArray();
        var typeUuids = types.ToDictionary(type => type.Id, type => StableUuid("note-type", type.Id));
        var json = ExportDeck(root, typeUuids, isRoot: true);
        json["note_models"] = new JsonArray(types.Select(type => ExportNoteType(type, typeUuids[type.Id])).ToArray());
        json["deck_configurations"] = new JsonArray();
        return json.ToJsonString(Options) + "\n";
    }

    /// <summary>Imports the supported CrowdAnki-style JSON subset and reports lossy concepts as structured diagnostics.</summary>
    /// <param name="json">A complete CrowdAnki-style deck object encoded as a .NET string.</param>
    /// <returns>A result containing a new deck hierarchy and all non-fatal compatibility diagnostics.</returns>
    /// <remarks>
    /// Deck and note-type IDs are deterministically derived from their <c>crowdanki_uuid</c> values, and note IDs are derived
    /// from note GUIDs. Missing deck UUIDs receive generated identities. Cards are regenerated from imported templates with
    /// new-card scheduling; original card IDs, scheduling, flags, and review history cannot be recovered. The result always
    /// includes diagnostic <c>CROWD001</c> to make that loss explicit. A deck that lists <c>media_files</c> also receives
    /// <c>CROWD002</c> because this string-only API cannot resolve sibling media payloads, and its returned media collection
    /// remains empty. Model field order, field editor settings, template markup, and browser-specific template formats are
    /// imported. Call <see cref="AnkiValidator.Validate(AnkiDeck)"/> if the imported graph must satisfy all write-time invariants
    /// before further use.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">
    /// The JSON is empty or malformed; a required deck, note-model, field, template, child, or note value is absent or has an
    /// incompatible JSON type or shape; a note references an unknown model; or a note's field count differs from its model.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// UUIDs are duplicated or imported names and values cannot form valid AnkiIO domain objects.
    /// </exception>
    public static CrowdAnkiImportResult Import(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Expected a CrowdAnki deck object.");
        var diagnostics = new List<AnkiDiagnostic>();
        var types = new Dictionary<string, AnkiNoteType>(StringComparer.Ordinal);
        foreach (var node in OptionalArray(root, "note_models"))
        {
            var model = RequiredObject(node, "A note_models entry must be an object.");
            var uuid = RequiredString(model, "crowdanki_uuid");
            var kind = OptionalValue(model, "type", 0) == 1 ? AnkiNoteTypeKind.Cloze : AnkiNoteTypeKind.Standard;
            var type = new AnkiNoteType(RequiredString(model, "name"), kind, AnkiId.FromStableValue("crowdanki-note-type", uuid))
            {
                Css = OptionalString(model, "css") ?? string.Empty,
            };
            foreach (var fieldNode in OptionalArray(model, "flds"))
            {
                var field = RequiredObject(fieldNode, "A field entry must be an object.");
                type.AddConfiguredField(new AnkiField(
                    RequiredString(field, "name"),
                    IsRightToLeft: OptionalValue(field, "rtl", false),
                    IsSticky: OptionalValue(field, "sticky", false),
                    Font: OptionalString(field, "font") ?? "Arial",
                    FontSize: OptionalValue(field, "size", 20)));
            }

            foreach (var templateNode in OptionalArray(model, "tmpls"))
            {
                var template = RequiredObject(templateNode, "A template entry must be an object.");
                var browserQuestion = OptionalString(template, "bqfmt");
                var browserAnswer = OptionalString(template, "bafmt");
                type.AddConfiguredTemplate(new AnkiCardTemplate(
                    RequiredString(template, "name"),
                    OptionalString(template, "qfmt") ?? string.Empty,
                    OptionalString(template, "afmt") ?? string.Empty,
                    string.IsNullOrEmpty(browserQuestion) ? null : browserQuestion,
                    string.IsNullOrEmpty(browserAnswer) ? null : browserAnswer));
            }

            types.Add(uuid, type);
        }

        var deck = ImportDeck(root, types, diagnostics, "$");
        diagnostics.Add(new(AnkiDiagnosticSeverity.Information, "CROWD001", "CrowdAnki JSON does not carry portable card scheduling or review history; imported cards use safe new-card scheduling.", SuggestedRemediation: "Use native JSON or APKG when scheduling must be preserved."));
        return new CrowdAnkiImportResult(deck, Array.AsReadOnly(diagnostics.ToArray()));
    }

    private static JsonObject ExportDeck(AnkiDeck deck, IReadOnlyDictionary<long, string> typeUuids, bool isRoot)
    {
        var result = new JsonObject
        {
            ["__type__"] = "Deck",
            ["crowdanki_uuid"] = StableUuid("deck", deck.Id),
            ["name"] = deck.Name,
            ["desc"] = deck.Description,
            ["deck_config_uuid"] = StableUuid("deck-config", 1),
            ["media_files"] = new JsonArray(deck.Media.Files.Select(media => JsonValue.Create(media.FileName)).ToArray()),
            ["notes"] = new JsonArray(deck.Notes.OrderBy(note => note.Guid, StringComparer.Ordinal).Select(note => ExportNote(note, typeUuids[note.NoteType.Id])).ToArray()),
            ["children"] = new JsonArray(deck.Subdecks.OrderBy(child => child.Name, StringComparer.Ordinal).Select(child => ExportDeck(child, typeUuids, isRoot: false)).ToArray()),
        };
        if (!isRoot)
        {
            result.Remove("desc");
        }

        return result;
    }

    private static JsonObject ExportNote(AnkiNote note, string typeUuid) => new()
    {
        ["__type__"] = "Note",
        ["guid"] = note.Guid,
        ["note_model_uuid"] = typeUuid,
        ["fields"] = new JsonArray(note.NoteType.Fields.Select(field => JsonValue.Create(note.Fields[field.Name])).ToArray()),
        ["tags"] = new JsonArray(note.Tags.Order(StringComparer.Ordinal).Select(tag => JsonValue.Create(tag)).ToArray()),
        ["flags"] = 0,
        ["data"] = string.Empty,
    };

    private static JsonObject ExportNoteType(AnkiNoteType type, string uuid) => new()
    {
        ["__type__"] = "NoteModel",
        ["crowdanki_uuid"] = uuid,
        ["name"] = type.Name,
        ["type"] = type.Kind == AnkiNoteTypeKind.Cloze ? 1 : 0,
        ["css"] = type.Css,
        ["flds"] = new JsonArray(type.Fields.Select((field, index) => new JsonObject
        {
            ["name"] = field.Name,
            ["ord"] = index,
            ["font"] = field.Font,
            ["size"] = field.FontSize,
            ["rtl"] = field.IsRightToLeft,
            ["sticky"] = field.IsSticky,
            ["media"] = new JsonArray(),
        }).ToArray()),
        ["tmpls"] = new JsonArray(type.Templates.Select((template, index) => new JsonObject
        {
            ["name"] = template.Name,
            ["ord"] = index,
            ["qfmt"] = template.QuestionFormat,
            ["afmt"] = template.AnswerFormat,
            ["bqfmt"] = template.BrowserQuestionFormat ?? string.Empty,
            ["bafmt"] = template.BrowserAnswerFormat ?? string.Empty,
        }).ToArray()),
    };

    private static AnkiDeck ImportDeck(JsonObject value, IReadOnlyDictionary<string, AnkiNoteType> types, List<AnkiDiagnostic> diagnostics, string location)
    {
        var uuid = OptionalString(value, "crowdanki_uuid") ?? StableUuid("anonymous-deck", AnkiId.New());
        var deck = new AnkiDeck(RequiredString(value, "name"), AnkiId.FromStableValue("crowdanki-deck", uuid))
        {
            Description = OptionalString(value, "desc") ?? string.Empty,
        };
        foreach (var noteNode in OptionalArray(value, "notes"))
        {
            var note = RequiredObject(noteNode, "A note entry must be an object.");
            var typeUuid = RequiredString(note, "note_model_uuid");
            if (!types.TryGetValue(typeUuid, out var type))
            {
                throw new JsonException($"Note references unknown note_model_uuid '{typeUuid}'.");
            }

            var values = OptionalArray(note, "fields").Select((field, index) => OptionalString(field, $"fields[{index}]") ?? string.Empty).ToArray();
            if (values.Length != type.Fields.Count)
            {
                throw new JsonException($"Note '{note["guid"]}' has {values.Length} fields; model '{type.Name}' requires {type.Fields.Count}.");
            }

            var fields = type.Fields.Select((field, index) => (field.Name, Value: values[index])).ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
            var tags = OptionalArray(note, "tags").Select((tag, index) => OptionalString(tag, $"tags[{index}]") ?? string.Empty).Where(tag => tag.Length > 0);
            var guid = RequiredString(note, "guid");
            deck.AddNote(type, fields, tags, guid, AnkiId.FromStableValue("crowdanki-note", guid));
        }

        if (OptionalArray(value, "media_files").Count > 0)
        {
            diagnostics.Add(new(AnkiDiagnosticSeverity.Warning, "CROWD002", "Media filenames were found, but CrowdAnki JSON stores payloads as sibling files and this string-only import cannot resolve them.", Location: location, DeckId: deck.Id, SuggestedRemediation: "Register media files from the CrowdAnki directory before package export."));
        }

        foreach (var childNode in OptionalArray(value, "children"))
        {
            deck.AddExistingSubdeck(ImportDeck(RequiredObject(childNode, "A child deck entry must be an object."), types, diagnostics, location + ".children"));
        }

        return deck;
    }

    private static JsonArray OptionalArray(JsonObject value, string name)
    {
        var node = value[name];
        return node switch
        {
            null => [],
            JsonArray array => array,
            _ => throw new JsonException($"Property '{name}' must be an array."),
        };
    }

    private static JsonObject RequiredObject(JsonNode? value, string message) => value as JsonObject ?? throw new JsonException(message);

    private static T OptionalValue<T>(JsonObject value, string name, T fallback)
    {
        var node = value[name];
        if (node is null)
        {
            return fallback;
        }

        if (node is JsonValue scalar && scalar.TryGetValue<T>(out var result))
        {
            return result;
        }

        throw new JsonException($"Property '{name}' has an incompatible JSON type.");
    }

    private static string? OptionalString(JsonObject value, string name) => OptionalString(value[name], name);

    private static string? OptionalString(JsonNode? value, string name)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var result))
        {
            return result;
        }

        throw new JsonException($"Property '{name}' must be a string.");
    }

    private static string RequiredString(JsonObject value, string name) =>
        OptionalString(value, name) is { } result && !string.IsNullOrWhiteSpace(result)
            ? result
            : throw new JsonException($"Required property '{name}' is missing or blank.");

    private static string StableUuid(string scope, long value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(scope + ":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return new Guid(bytes.AsSpan(0, 16)).ToString();
    }
}
