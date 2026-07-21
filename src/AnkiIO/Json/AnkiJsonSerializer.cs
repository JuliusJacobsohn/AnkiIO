using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnkiIO;

/// <summary>Serializes complete deck hierarchies using AnkiIO's deterministic, versioned native JSON format.</summary>
/// <remarks>
/// Native JSON is AnkiIO's loss-minimizing text interchange format for modeled decks, note types, notes, generated cards,
/// scheduling, and review history. It is an AnkiIO format, not an Anki package or CrowdAnki document. Version 1 does not
/// embed media payloads; transfer files from <see cref="AnkiDeck.Media"/> separately. Unknown properties are retained only
/// on deck objects through <see cref="AnkiDeck.UnknownData"/>. Unknown document, note-type, note, card, and scheduling
/// properties are ignored during import and are not reproduced.
/// </remarks>
public static class AnkiJsonSerializer
{
    private const string CurrentGeneratorName = "AnkiIO/1.0";

    /// <summary>Identifies the native JSON schema emitted and accepted by this release.</summary>
    /// <remarks>
    /// The value is written to the top-level <c>formatVersion</c> property. Readers reject other versions instead of
    /// attempting an implicit migration.
    /// </remarks>
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes a validated deck hierarchy to a culture-invariant JSON string.</summary>
    /// <param name="deck">The root deck whose entire descendant hierarchy will be serialized.</param>
    /// <returns>
    /// Indented UTF-16 JSON using camel-case property names and ending with a single line-feed character.
    /// </returns>
    /// <remarks>
    /// Note types, notes, tags, review records, subdecks, metadata keys, and extension-data keys are ordered before writing,
    /// so an unchanged hierarchy produces stable text. The method does not mutate <paramref name="deck"/>. Media filenames
    /// and bytes in <see cref="AnkiDeck.Media"/> are not included in native JSON version 1.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="deck"/> is <see langword="null"/>.</exception>
    /// <exception cref="AnkiValidationException">The hierarchy contains one or more error-severity validation diagnostics.</exception>
    public static string Serialize(AnkiDeck deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        EnsureValid(deck);
        return JsonSerializer.Serialize(ToDocument(deck), Options) + "\n";
    }

    /// <summary>Deserializes an AnkiIO native JSON document and validates the reconstructed hierarchy.</summary>
    /// <param name="json">A complete native JSON document encoded as a .NET string.</param>
    /// <returns>A new, mutable root deck containing the represented hierarchy.</returns>
    /// <remarks>
    /// The method accepts only <see cref="CurrentFormatVersion"/>. Note types are reconstructed first and shared by notes
    /// that reference the same ID. Media payloads are not represented by version 1, so the returned root has an empty media
    /// collection. Unrecognized deck-object properties are cloned into <see cref="AnkiDeck.UnknownData"/> for round trips.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">
    /// <paramref name="json"/> is empty or malformed, declares an unsupported format version, omits its root deck, references
    /// a missing note type, or gives a note a field count inconsistent with its note type.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The document contains duplicate note-type IDs or values that cannot form valid AnkiIO domain objects.
    /// </exception>
    /// <exception cref="AnkiValidationException">The reconstructed hierarchy contains error-severity validation diagnostics.</exception>
    public static AnkiDeck Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var document = JsonSerializer.Deserialize<NativeDocument>(json, Options) ?? throw new JsonException("The JSON document was empty.");
        return FromDocument(document);
    }

    /// <summary>Asynchronously writes a validated hierarchy as UTF-8 native JSON.</summary>
    /// <param name="deck">The root deck whose entire descendant hierarchy will be serialized.</param>
    /// <param name="destination">A writable stream positioned where the JSON document should begin.</param>
    /// <param name="cancellationToken">A token that can cancel asynchronous JSON serialization and stream writes.</param>
    /// <returns>A task that completes after the JSON document has been written.</returns>
    /// <remarks>
    /// The caller retains ownership of <paramref name="destination"/>. This method neither closes nor rewinds the stream and
    /// does not append the trailing line feed produced by <see cref="Serialize"/>. It validates before serialization and does
    /// not mutate <paramref name="deck"/>. Native JSON version 1 does not include media payloads.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="deck"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="AnkiValidationException">The hierarchy contains one or more error-severity validation diagnostics.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before the operation completes.</exception>
    /// <exception cref="NotSupportedException"><paramref name="destination"/> does not support writing.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="destination"/> is closed.</exception>
    /// <exception cref="IOException">An I/O error occurs while writing the stream.</exception>
    public static async Task WriteAsync(AnkiDeck deck, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        EnsureValid(deck);
        await JsonSerializer.SerializeAsync(destination, ToDocument(deck), Options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asynchronously reads and validates a native JSON hierarchy from a UTF-8 stream.</summary>
    /// <param name="source">A readable stream positioned at the beginning of a native JSON document.</param>
    /// <param name="cancellationToken">A token that can cancel asynchronous JSON parsing and stream reads.</param>
    /// <returns>A task whose result is a new, mutable root deck containing the represented hierarchy.</returns>
    /// <remarks>
    /// The caller retains ownership of <paramref name="source"/>. This method neither closes nor rewinds the stream; reading
    /// starts at its current position. Only <see cref="CurrentFormatVersion"/> is accepted. Version 1 returns an empty media
    /// collection because it does not embed media payloads.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">
    /// The stream is empty, contains malformed JSON, declares an unsupported format version, omits its root deck, references
    /// a missing note type, or gives a note a field count inconsistent with its note type.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The document contains duplicate note-type IDs or values that cannot form valid AnkiIO domain objects.
    /// </exception>
    /// <exception cref="AnkiValidationException">The reconstructed hierarchy contains error-severity validation diagnostics.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before the operation completes.</exception>
    /// <exception cref="NotSupportedException"><paramref name="source"/> does not support reading.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="source"/> is closed.</exception>
    /// <exception cref="IOException">An I/O error occurs while reading the stream.</exception>
    public static async Task<AnkiDeck> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var document = await JsonSerializer.DeserializeAsync<NativeDocument>(source, Options, cancellationToken).ConfigureAwait(false) ?? throw new JsonException("The JSON document was empty.");
        return FromDocument(document);
    }

    private static AnkiDeck FromDocument(NativeDocument document)
    {
        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw new JsonException($"Unsupported AnkiIO JSON format version {document.FormatVersion}; expected {CurrentFormatVersion}.");
        }

        var serializedTypes = document.NoteTypes ?? throw new JsonException("The noteTypes collection cannot be null.");
        var types = serializedTypes.ToDictionary(
            value => (value ?? throw new JsonException("The noteTypes collection contains a null entry.")).Id,
            value => FromDto(value!));
        var deck = FromDto(document.Deck ?? throw new JsonException("The root deck is missing."), types);
        EnsureValid(deck);
        return deck;
    }

    private static NativeDocument ToDocument(AnkiDeck deck)
    {
        var types = deck.Traverse().SelectMany(value => value.Notes).Select(note => note.NoteType).GroupBy(type => type.Id).Select(group => group.First()).OrderBy(type => type.Id).Select(ToDto).ToList();
        return new NativeDocument { FormatVersion = CurrentFormatVersion, Generator = CurrentGeneratorName, NoteTypes = types, Deck = ToDto(deck) };
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
        foreach (var field in value.Fields ?? throw new JsonException($"Note type {value.Id} has a null fields collection."))
        {
            type.AddConfiguredField(field ?? throw new JsonException($"Note type {value.Id} contains a null field definition."));
        }

        foreach (var template in value.Templates ?? throw new JsonException($"Note type {value.Id} has a null templates collection."))
        {
            type.AddConfiguredTemplate(template ?? throw new JsonException($"Note type {value.Id} contains a null template definition."));
        }

        return type;
    }

    private static AnkiDeck FromDto(DeckDto value, IReadOnlyDictionary<long, AnkiNoteType> noteTypes)
    {
        var deck = new AnkiDeck(value.Name, value.Id) { Description = value.Description };
        foreach (var pair in value.Metadata ?? throw new JsonException($"Deck {value.Id} has a null metadata object."))
        {
            deck.Metadata.Add(pair.Key, pair.Value ?? throw new JsonException($"Deck {value.Id} metadata '{pair.Key}' has a null value."));
        }

        if (value.ExtensionData is not null)
        {
            foreach (var pair in value.ExtensionData)
            {
                deck.UnknownData[pair.Key] = pair.Value.Clone();
            }
        }

        foreach (var valueNote in value.Notes ?? throw new JsonException($"Deck {value.Id} has a null notes collection."))
        {
            if (valueNote is null)
            {
                throw new JsonException($"Deck {value.Id} contains a null note.");
            }

            if (!noteTypes.TryGetValue(valueNote.NoteTypeId, out var type))
            {
                throw new JsonException($"Note {valueNote.Id} references missing note type {valueNote.NoteTypeId}.");
            }

            var serializedFields = valueNote.Fields ?? throw new JsonException($"Note {valueNote.Id} has a null fields collection.");
            if (serializedFields.Count != type.Fields.Count)
            {
                throw new JsonException($"Note {valueNote.Id} has {serializedFields.Count} fields; note type {type.Id} requires {type.Fields.Count}.");
            }

            var fields = type.Fields.Select((field, index) => (field.Name, Value: serializedFields[index] ?? throw new JsonException($"Note {valueNote.Id} contains a null value for field '{field.Name}'."))).ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
            var tags = valueNote.Tags ?? throw new JsonException($"Note {valueNote.Id} has a null tags collection.");
            if (tags.Any(tag => tag is null))
            {
                throw new JsonException($"Note {valueNote.Id} contains a null tag.");
            }

            var note = deck.AddNote(type, fields, tags, valueNote.Guid, valueNote.Id);
            var cards = valueNote.Cards ?? throw new JsonException($"Note {valueNote.Id} has a null cards collection.");
            note.RestoreCards(cards.Select(cardValue =>
            {
                var card = cardValue ?? throw new JsonException($"Note {valueNote.Id} contains a null card.");
                var scheduling = card.Scheduling ?? throw new JsonException($"Card {card.Id} has a null scheduling object.");
                var restored = new AnkiCard(card.Id, valueNote.Id, card.DeckId, card.TemplateOrdinal, scheduling) { Flag = card.Flag };
                foreach (var review in card.ReviewHistory ?? throw new JsonException($"Card {card.Id} has a null reviewHistory collection."))
                {
                    restored.ReviewHistory.Add(review ?? throw new JsonException($"Card {card.Id} contains a null review-history entry."));
                }

                return restored;
            }));
        }

        foreach (var child in value.Subdecks ?? throw new JsonException($"Deck {value.Id} has a null subdecks collection."))
        {
            deck.AddExistingSubdeck(FromDto(child ?? throw new JsonException($"Deck {value.Id} contains a null subdeck."), noteTypes));
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

}
