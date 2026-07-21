using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnkiIO;

/// <summary>Contains a deck reconstructed from CrowdAnki-style JSON and the compatibility diagnostics produced while importing it.</summary>
/// <param name="Deck">The new, mutable root deck reconstructed from the supported JSON concepts.</param>
/// <param name="Diagnostics">
/// Ordered informational and warning diagnostics describing concepts that were defaulted, ignored, or require external files.
/// </param>
/// <remarks>
/// A successful result can still contain lossy-import diagnostics. The deck and diagnostic list are retained by reference;
/// the record does not clone either value.
/// </remarks>
public sealed record CrowdAnkiImportResult(AnkiDeck Deck, IReadOnlyList<AnkiDiagnostic> Diagnostics)
{
    /// <summary>Gets the root deck reconstructed from the supported CrowdAnki-style concepts.</summary>
    /// <value>
    /// The mutable deck reference supplied to the positional constructor. The result does not clone or freeze the graph.
    /// </value>
    public AnkiDeck Deck { get; init; } = Deck;

    /// <summary>Gets the compatibility diagnostics produced while reconstructing <see cref="Deck"/>.</summary>
    /// <value>
    /// The ordered read-only-list reference supplied to the positional constructor. The result does not clone the list.
    /// </value>
    public IReadOnlyList<AnkiDiagnostic> Diagnostics { get; init; } = Diagnostics;
}

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
    /// imported. Call <see cref="AnkiValidator.Validate"/> if the imported graph must satisfy all write-time invariants
    /// before further use.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">
    /// The JSON is empty or malformed; a required deck, note-model, field, template, child, or note value is absent or has an
    /// incompatible JSON type; a note references an unknown model; or a note's field count differs from its model.
    /// </exception>
    /// <exception cref="InvalidOperationException">The JSON root or another required node has an incompatible object or array shape.</exception>
    /// <exception cref="ArgumentException">
    /// UUIDs are duplicated or imported names and values cannot form valid AnkiIO domain objects.
    /// </exception>
    public static CrowdAnkiImportResult Import(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("Expected a CrowdAnki deck object.");
        var diagnostics = new List<AnkiDiagnostic>();
        var types = new Dictionary<string, AnkiNoteType>(StringComparer.Ordinal);
        foreach (var node in root["note_models"]?.AsArray() ?? [])
        {
            var model = node?.AsObject() ?? throw new JsonException("A note_models entry was null.");
            var uuid = RequiredString(model, "crowdanki_uuid");
            var kind = model["type"]?.GetValue<int>() == 1 ? AnkiNoteTypeKind.Cloze : AnkiNoteTypeKind.Standard;
            var type = new AnkiNoteType(RequiredString(model, "name"), kind, AnkiId.FromStableValue("crowdanki-note-type", uuid))
            {
                Css = model["css"]?.GetValue<string>() ?? string.Empty,
            };
            foreach (var fieldNode in model["flds"]?.AsArray() ?? [])
            {
                var field = fieldNode?.AsObject() ?? throw new JsonException("A field was null.");
                type.AddConfiguredField(new AnkiField(
                    RequiredString(field, "name"),
                    IsRightToLeft: field["rtl"]?.GetValue<bool>() ?? false,
                    IsSticky: field["sticky"]?.GetValue<bool>() ?? false,
                    Font: field["font"]?.GetValue<string>() ?? "Arial",
                    FontSize: field["size"]?.GetValue<int>() ?? 20));
            }

            foreach (var templateNode in model["tmpls"]?.AsArray() ?? [])
            {
                var template = templateNode?.AsObject() ?? throw new JsonException("A template was null.");
                var browserQuestion = template["bqfmt"]?.GetValue<string>();
                var browserAnswer = template["bafmt"]?.GetValue<string>();
                type.AddConfiguredTemplate(new AnkiCardTemplate(
                    RequiredString(template, "name"),
                    template["qfmt"]?.GetValue<string>() ?? string.Empty,
                    template["afmt"]?.GetValue<string>() ?? string.Empty,
                    string.IsNullOrEmpty(browserQuestion) ? null : browserQuestion,
                    string.IsNullOrEmpty(browserAnswer) ? null : browserAnswer));
            }

            types.Add(uuid, type);
        }

        var deck = ImportDeck(root, types, diagnostics, "$");
        diagnostics.Add(new(AnkiDiagnosticSeverity.Information, "CROWD001", "CrowdAnki JSON does not carry portable card scheduling or review history; imported cards use safe new-card scheduling.", SuggestedRemediation: "Use native JSON or APKG when scheduling must be preserved."));
        return new CrowdAnkiImportResult(deck, diagnostics);
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
        var uuid = value["crowdanki_uuid"]?.GetValue<string>() ?? StableUuid("anonymous-deck", AnkiId.New());
        var deck = new AnkiDeck(RequiredString(value, "name"), AnkiId.FromStableValue("crowdanki-deck", uuid))
        {
            Description = value["desc"]?.GetValue<string>() ?? string.Empty,
        };
        foreach (var noteNode in value["notes"]?.AsArray() ?? [])
        {
            var note = noteNode?.AsObject() ?? throw new JsonException("A note was null.");
            var typeUuid = RequiredString(note, "note_model_uuid");
            if (!types.TryGetValue(typeUuid, out var type))
            {
                throw new JsonException($"Note references unknown note_model_uuid '{typeUuid}'.");
            }

            var values = note["fields"]?.AsArray().Select(field => field?.GetValue<string>() ?? string.Empty).ToArray() ?? [];
            if (values.Length != type.Fields.Count)
            {
                throw new JsonException($"Note '{note["guid"]}' has {values.Length} fields; model '{type.Name}' requires {type.Fields.Count}.");
            }

            var fields = type.Fields.Select((field, index) => (field.Name, Value: values[index])).ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
            var tags = note["tags"]?.AsArray().Select(tag => tag?.GetValue<string>() ?? string.Empty).Where(tag => tag.Length > 0) ?? [];
            var guid = RequiredString(note, "guid");
            deck.AddNote(type, fields, tags, guid, AnkiId.FromStableValue("crowdanki-note", guid));
        }

        if ((value["media_files"]?.AsArray().Count ?? 0) > 0)
        {
            diagnostics.Add(new(AnkiDiagnosticSeverity.Warning, "CROWD002", "Media filenames were found, but CrowdAnki JSON stores payloads as sibling files and this string-only import cannot resolve them.", Location: location, DeckId: deck.Id, SuggestedRemediation: "Register media files from the CrowdAnki directory before package export."));
        }

        foreach (var childNode in value["children"]?.AsArray() ?? [])
        {
            deck.AddExistingSubdeck(ImportDeck(childNode?.AsObject() ?? throw new JsonException("A child deck was null."), types, diagnostics, location + ".children"));
        }

        return deck;
    }

    private static string RequiredString(JsonObject value, string name) => value[name]?.GetValue<string>() is { Length: > 0 } result ? result : throw new JsonException($"Required property '{name}' is missing or blank.");

    private static string StableUuid(string scope, long value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(scope + ":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return new Guid(bytes.AsSpan(0, 16)).ToString();
    }
}
