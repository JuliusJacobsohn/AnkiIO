using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnkiIO;

/// <summary>Represents one Anki deck, its directly assigned notes, and its nested subdecks.</summary>
/// <remarks>
/// Deck names are stored as local hierarchy segments. Build a hierarchy with <see cref="AddSubdeck"/> instead of putting
/// Anki's <c>::</c> separator in <see cref="Name"/>. A deck owns notes added directly to it; use <see cref="Traverse"/>
/// when an operation must include descendants. Instances are mutable and are not designed for concurrent mutation.
/// </remarks>
public sealed partial class AnkiDeck
{
    private readonly List<AnkiDeck> subdecks = [];
    private readonly List<AnkiNote> notes = [];
    private readonly ReadOnlyCollection<AnkiDeck> subdecksView;
    private readonly ReadOnlyCollection<AnkiNote> notesView;
    private ConventionalNoteTypeCache conventionalNoteTypes;

    /// <summary>Initializes a top-level deck with an empty note and subdeck collection.</summary>
    /// <param name="name">A non-empty local deck segment; use <see cref="AddSubdeck"/> to build hierarchy.</param>
    /// <param name="id">
    /// An optional stable numeric deck ID for repeatable imports; when omitted, <see cref="AnkiId.New"/> generates one.
    /// </param>
    /// <remarks>The deck's <see cref="Media"/> collection is initialized empty.</remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank or contains Anki's <c>::</c> hierarchy separator.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public AnkiDeck(string name, long? id = null)
        : this(name, id, new ConventionalNoteTypeCache())
    {
    }

    private AnkiDeck(string name, long? id, ConventionalNoteTypeCache conventionalNoteTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains("::", StringComparison.Ordinal))
        {
            throw new ArgumentException("A deck segment cannot contain '::'. Build hierarchy with AddSubdeck().", nameof(name));
        }

        Name = name;
        Id = id ?? AnkiId.New();
        Media = new AnkiMediaCollection();
        subdecksView = subdecks.AsReadOnly();
        notesView = notes.AsReadOnly();
        this.conventionalNoteTypes = conventionalNoteTypes;
    }

    /// <summary>Gets the stable numeric deck identifier.</summary>
    /// <value>The caller-supplied identifier, or a process-generated positive identifier.</value>
    public long Id { get; }

    /// <summary>Gets the local name segment.</summary>
    /// <value>A non-empty segment that never contains Anki's <c>::</c> hierarchy separator.</value>
    public string Name { get; }

    /// <summary>Gets or sets user-visible HTML description.</summary>
    /// <value>HTML displayed for the deck. The default is an empty string.</value>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets custom string metadata preserved by native JSON.</summary>
    /// <value>
    /// A live, mutable, ordinal-keyed dictionary. Legacy APKG and CrowdAnki-inspired writers do not preserve these values.
    /// </value>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets unrecognized native-JSON properties retained for forward-compatible round trips.</summary>
    /// <value>
    /// A live, mutable, ordinal-keyed dictionary of cloned JSON values. Preservation is limited to unknown properties on
    /// deck objects in the native AnkiIO JSON format.
    /// </value>
    public IDictionary<string, JsonElement> UnknownData { get; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>Gets media registered directly on this deck.</summary>
    /// <value>
    /// A mutable media collection created with the deck. Package writing aggregates media from this deck and every
    /// descendant; registrations are not automatically shared between parent and child decks.
    /// </value>
    public AnkiMediaCollection Media { get; }

    /// <summary>Gets direct child decks.</summary>
    /// <value>A live read-only view in insertion order. It is not an immutable snapshot.</value>
    public IReadOnlyList<AnkiDeck> Subdecks => subdecksView;

    /// <summary>Gets notes assigned directly to this deck.</summary>
    /// <value>A live read-only view in insertion order. Descendant notes are not included.</value>
    public IReadOnlyList<AnkiNote> Notes => notesView;

    /// <summary>Creates and adds a direct child deck.</summary>
    /// <param name="name">A non-empty local name segment without Anki's <c>::</c> hierarchy separator.</param>
    /// <param name="id">
    /// An optional stable numeric deck ID for repeatable imports; when omitted, <see cref="AnkiId.New"/> generates one.
    /// </param>
    /// <returns>The newly created child deck, ready for notes or further nested decks.</returns>
    /// <remarks>
    /// Sibling names are compared without regard to case. Conventional note types created by the convenience note methods
    /// are shared with the child so notes throughout one hierarchy reuse the same models.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is blank, contains <c>::</c>, or duplicates an existing direct child name ignoring case.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public AnkiDeck AddSubdeck(string name, long? id = null)
    {
        if (subdecks.Any(deck => string.Equals(deck.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Subdeck '{name}' already exists.", nameof(name));
        }

        var deck = new AnkiDeck(name, id, conventionalNoteTypes);
        subdecks.Add(deck);
        return deck;
    }

    /// <summary>Adds a note and generates its cards using safe new-card scheduling.</summary>
    /// <param name="noteType">The fully configured note type.</param>
    /// <param name="fields">Values keyed by field name.</param>
    /// <param name="tags">Optional non-empty tags without whitespace; duplicates are collapsed ordinally.</param>
    /// <param name="guid">An optional stable Anki GUID.</param>
    /// <param name="id">A stable note ID, or <see langword="null"/> to generate one.</param>
    /// <returns>The created note.</returns>
    /// <remarks>
    /// Missing defined fields are stored as empty strings; unknown field names are rejected. The note retains the supplied
    /// mutable note type by reference, and cards are generated immediately from its current templates or cloze indexes.
    /// An exact conventional Basic, reversed, or Cloze definition becomes the corresponding helper type for this hierarchy.
    /// Finish configuring the note type before calling this method. This instance is not safe for concurrent mutation.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="noteType"/> or <paramref name="fields"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fields"/> contains a name not defined by <paramref name="noteType"/>, <paramref name="tags"/>
    /// contains a blank or whitespace-containing tag, or a Cloze <c>Text</c> value contains a numeric index outside the
    /// range supported by <see cref="int"/>.
    /// </exception>
    public AnkiNote AddNote(AnkiNoteType noteType, IReadOnlyDictionary<string, string> fields, IEnumerable<string>? tags = null, string? guid = null, long? id = null)
    {
        ArgumentNullException.ThrowIfNull(noteType);
        ArgumentNullException.ThrowIfNull(fields);
        var note = new AnkiNote(noteType, fields, tags, id, guid);
        note.GenerateCards(Id, nameof(fields));
        notes.Add(note);
        conventionalNoteTypes.Observe(noteType);
        return note;
    }

    /// <summary>Removes a note and its generated cards from this deck.</summary>
    /// <param name="note">The note to remove.</param>
    /// <returns><see langword="true"/> when the same note instance was directly owned by this deck; otherwise, <see langword="false"/>.</returns>
    /// <remarks>The detached note and its cards remain usable. This method does not search subdecks.</remarks>
    public bool RemoveNote(AnkiNote note) => notes.Remove(note);

    internal void AddExistingSubdeck(AnkiDeck deck)
    {
        deck.UseConventionalNoteTypes(conventionalNoteTypes);
        subdecks.Add(deck);
    }

    internal void AddExistingNote(AnkiNote note)
    {
        note.AttachToDeck(Id);
        notes.Add(note);
        conventionalNoteTypes.Observe(note.NoteType);
    }

    private void UseConventionalNoteTypes(ConventionalNoteTypeCache cache)
    {
        conventionalNoteTypes.CopyMissingTo(cache);
        conventionalNoteTypes = cache;
        foreach (var note in notes)
        {
            cache.Observe(note.NoteType);
        }

        foreach (var subdeck in subdecks)
        {
            subdeck.UseConventionalNoteTypes(cache);
        }
    }

    /// <summary>Enumerates this deck followed by every descendant in stable insertion order.</summary>
    /// <returns>A lazy depth-first sequence beginning with this deck.</returns>
    /// <remarks>Do not mutate the hierarchy while enumerating the returned sequence.</remarks>
    public IEnumerable<AnkiDeck> Traverse()
    {
        yield return this;
        foreach (var child in subdecks)
        {
            foreach (var descendant in child.Traverse())
            {
                yield return descendant;
            }
        }
    }
}

/// <summary>Stores field values and tags from which one or more cards are generated.</summary>
/// <remarks>
/// Ordinary callers should create notes through <see cref="AnkiDeck.AddNote"/> or its convenience helpers so cards are
/// generated and the note is attached to a deck. Instances are mutable and are not safe for concurrent mutation.
/// </remarks>
public sealed partial class AnkiNote
{
    private readonly Dictionary<string, string> fields;
    private readonly HashSet<string> tags;
    private readonly List<AnkiCard> cards = [];
    private readonly ReadOnlyDictionary<string, string> fieldsView;
    private readonly ReadOnlyCollection<AnkiCard> cardsView;
    private long? generatedDeckId;
    private bool cardsHaveBeenGenerated;

    /// <summary>Initializes a detached note while preserving its stable identity.</summary>
    /// <param name="noteType">The configured note type retained by reference.</param>
    /// <param name="fields">Field values keyed by exact, ordinal field name. Missing defined fields become empty strings.</param>
    /// <param name="tags">
    /// Optional non-empty tags without whitespace; duplicates are collapsed using ordinal comparison. The whitespace
    /// restriction ensures tags can be represented losslessly by legacy Anki package storage.
    /// </param>
    /// <param name="id">An optional stable numeric note identifier, or <see langword="null"/> to generate one.</param>
    /// <param name="guid">An optional stable Anki import GUID, or <see langword="null"/> or blank to generate one.</param>
    /// <remarks>
    /// This constructor does not generate cards or attach the note to a deck. After all arguments have been validated, it
    /// freezes <paramref name="noteType"/> against later field, template, and CSS changes.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="noteType"/> or <paramref name="fields"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fields"/> contains a name absent from <paramref name="noteType"/>, or <paramref name="tags"/>
    /// contains a blank or whitespace-containing value.
    /// </exception>
    public AnkiNote(AnkiNoteType noteType, IReadOnlyDictionary<string, string> fields, IEnumerable<string>? tags = null, long? id = null, string? guid = null)
    {
        ArgumentNullException.ThrowIfNull(noteType);
        ArgumentNullException.ThrowIfNull(fields);
        Id = id ?? AnkiId.New();
        Guid = string.IsNullOrWhiteSpace(guid) ? System.Guid.NewGuid().ToString("N")[..10] : guid;
        this.fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in noteType.Fields)
        {
            this.fields[field.Name] = fields.TryGetValue(field.Name, out var value) ? value : string.Empty;
        }

        var unknown = fields.Keys.FirstOrDefault(key => !this.fields.ContainsKey(key));
        if (unknown is not null)
        {
            throw new ArgumentException($"Field '{unknown}' is not defined by note type '{noteType.Name}'.", nameof(fields));
        }

        this.tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags ?? [])
        {
            ValidateTag(tag, nameof(tags));
            this.tags.Add(tag);
        }

        fieldsView = new ReadOnlyDictionary<string, string>(this.fields);
        cardsView = cards.AsReadOnly();
        NoteType = noteType;
        noteType.Freeze();
    }

    /// <summary>Gets the stable numeric note identifier.</summary>
    /// <value>The caller-supplied identifier, or a process-generated positive identifier.</value>
    public long Id { get; }

    /// <summary>Gets the stable import identity used by Anki.</summary>
    /// <value>The caller-supplied GUID or a generated ten-character identifier.</value>
    public string Guid { get; }

    /// <summary>Gets the note type that defines field order and card generation.</summary>
    /// <value>The same instance supplied to the constructor, frozen against structural changes when this note was created.</value>
    public AnkiNoteType NoteType { get; }

    /// <summary>Gets field values by name.</summary>
    /// <value>A live read-only view. Use <see cref="SetField"/> to change a value.</value>
    public IReadOnlyDictionary<string, string> Fields => fieldsView;

    /// <summary>Gets tags in ordinal sorted order for deterministic serialization.</summary>
    /// <value>A newly allocated sorted snapshot.</value>
    public IReadOnlyCollection<string> Tags => Array.AsReadOnly(tags.Order(StringComparer.Ordinal).ToArray());

    /// <summary>Gets cards currently generated for the note.</summary>
    /// <value>A live read-only view in template or cloze-index order.</value>
    public IReadOnlyList<AnkiCard> Cards => cardsView;

    /// <summary>Replaces a defined field value.</summary>
    /// <param name="name">The exact, case-sensitive field name.</param>
    /// <param name="value">The replacement value, including any Anki HTML or template markup.</param>
    /// <remarks>
    /// Changing a generated Cloze note's <c>Text</c> immediately reconciles <see cref="Cards"/> with its distinct positive
    /// indexes. Cards for indexes that remain are retained with their identity, scheduling, flag, and review history;
    /// removed indexes lose their cards and new indexes receive safe new-card state. Other field changes do not affect cards.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not defined by <see cref="NoteType"/>.</exception>
    public void SetField(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!fields.ContainsKey(name))
        {
            throw new ArgumentException($"Field '{name}' is not defined by note type '{NoteType.Name}'.", nameof(name));
        }

        int[]? clozeOrdinals = null;
        if (cardsHaveBeenGenerated
            && NoteType.Kind == AnkiNoteTypeKind.Cloze
            && string.Equals(name, "Text", StringComparison.Ordinal))
        {
            clozeOrdinals = GetClozeOrdinals(value, nameof(value));
        }

        fields[name] = value;
        if (clozeOrdinals is not null)
        {
            ReconcileClozeCards(clozeOrdinals, generatedDeckId ?? throw new InvalidOperationException("The generated note is not associated with a deck."));
        }
    }

    /// <summary>Adds a non-empty, legacy-package-safe note tag.</summary>
    /// <param name="tag">The case-sensitive tag text without whitespace. Existing identical tags are left unchanged.</param>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is blank or contains whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    public void AddTag(string tag)
    {
        ValidateTag(tag, nameof(tag));
        tags.Add(tag);
    }

    /// <summary>Removes a note tag using ordinal comparison.</summary>
    /// <param name="tag">The exact, case-sensitive tag to remove.</param>
    /// <returns><see langword="true"/> when the tag existed and was removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveTag(string tag) => tags.Remove(tag);

    internal void GenerateCards(long deckId, string invalidClozeParameterName)
    {
        if (NoteType.Kind == AnkiNoteTypeKind.Cloze)
        {
            fields.TryGetValue("Text", out var text);
            var ordinals = GetClozeOrdinals(text ?? string.Empty, invalidClozeParameterName);
            cards.Clear();
            cards.AddRange(ordinals.Select(ordinal => CreateNewCard(deckId, ordinal)));
            generatedDeckId = deckId;
            cardsHaveBeenGenerated = true;
            return;
        }

        cards.Clear();
        for (var ordinal = 0; ordinal < NoteType.Templates.Count; ordinal++)
        {
            cards.Add(CreateNewCard(deckId, ordinal));
        }

        generatedDeckId = deckId;
        cardsHaveBeenGenerated = true;
    }

    internal void RestoreCards(IEnumerable<AnkiCard> restored)
    {
        ArgumentNullException.ThrowIfNull(restored);
        var restoredCards = restored.ToArray();
        cards.Clear();
        cards.AddRange(restoredCards);
        generatedDeckId ??= restoredCards.FirstOrDefault()?.DeckId;
        cardsHaveBeenGenerated = true;
    }

    internal void AttachToDeck(long deckId) => generatedDeckId ??= deckId;

    private static int[] GetClozeOrdinals(string text, string invalidClozeParameterName) => ClozePattern().Matches(text)
        .Select(match => int.TryParse(
            match.Groups[1].Value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var index)
            ? index
            : throw new ArgumentException(
                $"The cloze Text field contains index '{match.Groups[1].Value}', which is outside the range supported by System.Int32.",
                invalidClozeParameterName))
        .Where(index => index > 0)
        .Select(index => index - 1)
        .Distinct()
        .Order()
        .ToArray();

    private void ReconcileClozeCards(IEnumerable<int> ordinals, long deckId)
    {
        var existing = cards
            .GroupBy(card => card.TemplateOrdinal)
            .ToDictionary(group => group.Key, group => group.First());
        var reconciled = ordinals
            .Select(ordinal => existing.GetValueOrDefault(ordinal) ?? CreateNewCard(deckId, ordinal))
            .ToArray();
        cards.Clear();
        cards.AddRange(reconciled);
    }

    private AnkiCard CreateNewCard(long deckId, int ordinal) => new(AnkiId.New(), Id, deckId, ordinal, AnkiScheduling.New);

    private static void ValidateTag(string tag, string parameterName)
    {
        if (tag is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (string.IsNullOrWhiteSpace(tag) || tag.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Anki tags cannot be blank or contain whitespace.", parameterName);
        }
    }

    [GeneratedRegex(@"\{\{c(\d+)::", RegexOptions.CultureInvariant)]
    private static partial Regex ClozePattern();
}

/// <summary>Represents a study prompt generated from a note template.</summary>
/// <remarks>
/// Mutable scheduling, deck, flag, and history values are accepted without immediate validation. Call
/// <see cref="AnkiValidator.Validate"/> before relying on or exporting modified card state. Instances are not thread-safe.
/// </remarks>
public sealed class AnkiCard
{
    private AnkiScheduling scheduling;

    /// <summary>Initializes a card for advanced import and adapter scenarios.</summary>
    /// <param name="id">The stable numeric card identifier.</param>
    /// <param name="noteId">The identifier of the owning note.</param>
    /// <param name="deckId">The identifier of the deck in which the card currently appears.</param>
    /// <param name="templateOrdinal">The zero-based standard-template ordinal or cloze index.</param>
    /// <param name="scheduling">The current scheduling state retained by reference.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="templateOrdinal"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="scheduling"/> is <see langword="null"/>.</exception>
    public AnkiCard(long id, long noteId, long deckId, int templateOrdinal, AnkiScheduling scheduling)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(templateOrdinal);

        Id = id;
        NoteId = noteId;
        DeckId = deckId;
        TemplateOrdinal = templateOrdinal;
        this.scheduling = scheduling ?? throw new ArgumentNullException(nameof(scheduling));
    }

    /// <summary>Gets the stable numeric card identifier.</summary>
    /// <value>The identifier supplied to the constructor.</value>
    public long Id { get; }

    /// <summary>Gets the owning note identifier.</summary>
    /// <value>The note identifier supplied to the constructor.</value>
    public long NoteId { get; }

    /// <summary>Gets or sets the deck in which this card appears.</summary>
    /// <value>A deck identifier. Assignment is unchecked and may not match the containing object graph.</value>
    public long DeckId { get; set; }

    /// <summary>Gets the template ordinal, or zero-based cloze index.</summary>
    /// <value>A non-negative ordinal fixed at construction.</value>
    public int TemplateOrdinal { get; }

    /// <summary>Gets or sets scheduler state.</summary>
    /// <value>A non-null state object. Assignment is unchecked; validation is deferred to <see cref="AnkiValidator"/>.</value>
    public AnkiScheduling Scheduling
    {
        get => scheduling;
        set => scheduling = value ?? throw new ArgumentNullException(nameof(value), "Scheduling cannot be null.");
    }

    /// <summary>Gets or sets Anki's low three-bit color flag value.</summary>
    /// <value>An integer expected to be between zero and seven; validation is deferred.</value>
    public int Flag { get; set; }

    /// <summary>Gets review-history records retained for round trips.</summary>
    /// <value>A live mutable list owned by the card. Entries are not validated when added.</value>
    public IList<AnkiReviewLog> ReviewHistory { get; } = new List<AnkiReviewLog>();
}
