using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnkiIO;

/// <summary>Reads and writes AnkiIO's deterministic, versioned native JSON format.</summary>
public static class AnkiJsonSerializer
{
    /// <summary>Gets the current native JSON format version.</summary>
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes a validated deck hierarchy to culture-invariant JSON.</summary>
    /// <exception cref="AnkiValidationException">The hierarchy contains unsafe or inconsistent data.</exception>
    public static string Serialize(AnkiDeck deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        EnsureValid(deck);
        return JsonSerializer.Serialize(ToDocument(deck), Options) + "\n";
    }

    /// <summary>Deserializes native JSON and rejects unsupported versions or invalid content.</summary>
    /// <exception cref="JsonException">The document is malformed or unsupported.</exception>
    /// <exception cref="AnkiValidationException">The represented hierarchy violates domain invariants.</exception>
    public static AnkiDeck Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var document = JsonSerializer.Deserialize<NativeDocument>(json, Options) ?? throw new JsonException("The JSON document was empty.");
        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw new JsonException($"Unsupported AnkiIO JSON format version {document.FormatVersion}; expected {CurrentFormatVersion}.");
        }

        var noteTypes = document.NoteTypes.ToDictionary(value => value.Id, FromDto);
        var deck = FromDto(document.Deck ?? throw new JsonException("The root deck is missing."), noteTypes);
        EnsureValid(deck);
        return deck;
    }

    /// <summary>Writes native JSON asynchronously. The caller retains ownership of the stream.</summary>
    public static async Task WriteAsync(AnkiDeck deck, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        EnsureValid(deck);
        await JsonSerializer.SerializeAsync(destination, ToDocument(deck), Options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads native JSON asynchronously. The caller retains ownership of the stream.</summary>
    public static async Task<AnkiDeck> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var document = await JsonSerializer.DeserializeAsync<NativeDocument>(source, Options, cancellationToken).ConfigureAwait(false) ?? throw new JsonException("The JSON document was empty.");
        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw new JsonException($"Unsupported AnkiIO JSON format version {document.FormatVersion}; expected {CurrentFormatVersion}.");
        }

        var types = document.NoteTypes.ToDictionary(value => value.Id, FromDto);
        var deck = FromDto(document.Deck ?? throw new JsonException("The root deck is missing."), types);
        EnsureValid(deck);
        return deck;
    }

    private static NativeDocument ToDocument(AnkiDeck deck)
    {
        var types = deck.Traverse().SelectMany(value => value.Notes).Select(note => note.NoteType).GroupBy(type => type.Id).Select(group => group.First()).OrderBy(type => type.Id).Select(ToDto).ToList();
        return new NativeDocument { FormatVersion = CurrentFormatVersion, Generator = "AnkiIO/0.1", NoteTypes = types, Deck = ToDto(deck) };
    }

    private static NoteTypeDto ToDto(AnkiNoteType value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        Kind = value.Kind,
        Css = value.Css,
        Fields = value.Fields.ToList(),
        Templates = value.Templates.ToList(),
    };

    private static DeckDto ToDto(AnkiDeck value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        Description = value.Description,
        Metadata = new SortedDictionary<string, string>(value.Metadata, StringComparer.Ordinal),
        Notes = value.Notes.OrderBy(note => note.Id).Select(ToDto).ToList(),
        Subdecks = value.Subdecks.OrderBy(deck => deck.Name, StringComparer.Ordinal).ThenBy(deck => deck.Id).Select(ToDto).ToList(),
        ExtensionData = value.UnknownData.Count == 0 ? null : new SortedDictionary<string, JsonElement>(value.UnknownData, StringComparer.Ordinal),
    };

    private static NoteDto ToDto(AnkiNote value) => new()
    {
        Id = value.Id,
        Guid = value.Guid,
        NoteTypeId = value.NoteType.Id,
        Fields = value.NoteType.Fields.Select(field => value.Fields[field.Name]).ToList(),
        Tags = value.Tags.Order(StringComparer.Ordinal).ToList(),
        Cards = value.Cards.OrderBy(card => card.TemplateOrdinal).Select(card => new CardDto
        {
            Id = card.Id,
            DeckId = card.DeckId,
            TemplateOrdinal = card.TemplateOrdinal,
            Flag = card.Flag,
            Scheduling = card.Scheduling,
            ReviewHistory = card.ReviewHistory.OrderBy(review => review.Id).ToList(),
        }).ToList(),
    };

    private static AnkiNoteType FromDto(NoteTypeDto value)
    {
        var type = new AnkiNoteType(value.Name, value.Kind, value.Id) { Css = value.Css };
        foreach (var field in value.Fields)
        {
            type.AddField(field.Name);
        }

        foreach (var template in value.Templates)
        {
            type.AddTemplate(template.Name, template.QuestionFormat, template.AnswerFormat);
        }

        return type;
    }

    private static AnkiDeck FromDto(DeckDto value, IReadOnlyDictionary<long, AnkiNoteType> noteTypes)
    {
        var deck = new AnkiDeck(value.Name, value.Id) { Description = value.Description };
        foreach (var pair in value.Metadata)
        {
            deck.Metadata.Add(pair.Key, pair.Value);
        }

        if (value.ExtensionData is not null)
        {
            foreach (var pair in value.ExtensionData)
            {
                deck.UnknownData[pair.Key] = pair.Value.Clone();
            }
        }

        foreach (var valueNote in value.Notes)
        {
            if (!noteTypes.TryGetValue(valueNote.NoteTypeId, out var type))
            {
                throw new JsonException($"Note {valueNote.Id} references missing note type {valueNote.NoteTypeId}.");
            }

            if (valueNote.Fields.Count != type.Fields.Count)
            {
                throw new JsonException($"Note {valueNote.Id} has {valueNote.Fields.Count} fields; note type {type.Id} requires {type.Fields.Count}.");
            }

            var fields = type.Fields.Select((field, index) => (field.Name, Value: valueNote.Fields[index])).ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
            var note = deck.AddNote(type, fields, valueNote.Tags, valueNote.Guid, valueNote.Id);
            note.RestoreCards(valueNote.Cards.Select(card =>
            {
                var restored = new AnkiCard(card.Id, valueNote.Id, card.DeckId, card.TemplateOrdinal, card.Scheduling) { Flag = card.Flag };
                foreach (var review in card.ReviewHistory)
                {
                    restored.ReviewHistory.Add(review);
                }

                return restored;
            }));
        }

        foreach (var child in value.Subdecks)
        {
            deck.AddExistingSubdeck(FromDto(child, noteTypes));
        }

        return deck;
    }

    private static void EnsureValid(AnkiDeck deck)
    {
        var result = AnkiValidator.Validate(deck);
        if (!result.IsValid)
        {
            throw new AnkiValidationException(result);
        }
    }

    private sealed class NativeDocument
    {
        public int FormatVersion { get; set; }
        public string Generator { get; set; } = string.Empty;
        public List<NoteTypeDto> NoteTypes { get; set; } = [];
        public DeckDto? Deck { get; set; }
    }

    private sealed class NoteTypeDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public AnkiNoteTypeKind Kind { get; set; }
        public string Css { get; set; } = string.Empty;
        public List<AnkiField> Fields { get; set; } = [];
        public List<AnkiCardTemplate> Templates { get; set; } = [];
    }

    private sealed class DeckDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public SortedDictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
        public List<NoteDto> Notes { get; set; } = [];
        public List<DeckDto> Subdecks { get; set; } = [];
        [JsonExtensionData]
        public SortedDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private sealed class NoteDto
    {
        public long Id { get; set; }
        public string Guid { get; set; } = string.Empty;
        public long NoteTypeId { get; set; }
        public List<string> Fields { get; set; } = [];
        public List<string> Tags { get; set; } = [];
        public List<CardDto> Cards { get; set; } = [];
    }

    private sealed class CardDto
    {
        public long Id { get; set; }
        public long DeckId { get; set; }
        public int TemplateOrdinal { get; set; }
        public int Flag { get; set; }
        public AnkiScheduling Scheduling { get; set; } = AnkiScheduling.New;
        public List<AnkiReviewLog> ReviewHistory { get; set; } = [];
    }
}

/// <summary>Signals that inspectable validation errors prevent a safe operation.</summary>
public sealed class AnkiValidationException : Exception
{
    /// <summary>Initializes the exception from a validation pass.</summary>
    public AnkiValidationException(AnkiValidationResult validationResult)
        : base($"Anki content validation failed with {validationResult.Diagnostics.Count(value => value.Severity == AnkiDiagnosticSeverity.Error)} error(s).") => ValidationResult = validationResult;

    /// <summary>Gets the complete structured validation result.</summary>
    public AnkiValidationResult ValidationResult { get; }
}
