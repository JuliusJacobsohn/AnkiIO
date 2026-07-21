using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnkiIO;

/// <summary>Contains a CrowdAnki-inspired import result and non-fatal compatibility diagnostics.</summary>
public sealed record CrowdAnkiImportResult(AnkiDeck Deck, IReadOnlyList<AnkiDiagnostic> Diagnostics);

/// <summary>Maps the documented AnkiIO domain to a conservative subset of CrowdAnki's JSON shape.</summary>
/// <remarks>This adapter is independently implemented from observed public behavior. It does not claim full CrowdAnki compatibility and intentionally omits scheduling, card IDs, and review history because CrowdAnki JSON does not reliably represent them.</remarks>
public static class CrowdAnkiJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Exports a hierarchy using CrowdAnki-style type markers, nested children, note models, notes, and media references.</summary>
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

    /// <summary>Imports the supported CrowdAnki subset and reports ignored or lossy concepts as diagnostics.</summary>
    /// <exception cref="JsonException">Required deck, note-model, or note fields are malformed.</exception>
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
                type.AddField(RequiredString(fieldNode?.AsObject() ?? throw new JsonException("A field was null."), "name"));
            }

            foreach (var templateNode in model["tmpls"]?.AsArray() ?? [])
            {
                var template = templateNode?.AsObject() ?? throw new JsonException("A template was null.");
                type.AddTemplate(RequiredString(template, "name"), template["qfmt"]?.GetValue<string>() ?? string.Empty, template["afmt"]?.GetValue<string>() ?? string.Empty);
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
