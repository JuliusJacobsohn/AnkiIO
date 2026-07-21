using System.Text.RegularExpressions;

namespace AnkiIO;

/// <summary>Represents an Anki deck hierarchy and owns the notes assigned to it.</summary>
public sealed class AnkiDeck
{
    private readonly List<AnkiDeck> subdecks = [];
    private readonly List<AnkiNote> notes = [];

    /// <summary>Initializes a deck.</summary>
    /// <param name="name">A local deck segment; use <see cref="AddSubdeck"/> to build hierarchy.</param>
    /// <param name="id">A stable deck ID, or <see langword="null"/> to generate one.</param>
    public AnkiDeck(string name, long? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains("::", StringComparison.Ordinal))
        {
            throw new ArgumentException("A deck segment cannot contain '::'. Build hierarchy with AddSubdeck().", nameof(name));
        }

        Name = name;
        Id = id ?? AnkiId.New();
        Media = new AnkiMediaCollection();
    }

    /// <summary>Gets the stable numeric deck identifier.</summary>
    public long Id { get; }

    /// <summary>Gets the local name segment.</summary>
    public string Name { get; }

    /// <summary>Gets or sets user-visible HTML description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets custom string metadata preserved by native JSON.</summary>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the media collection owned by this deck root.</summary>
    public AnkiMediaCollection Media { get; }

    /// <summary>Gets direct child decks.</summary>
    public IReadOnlyList<AnkiDeck> Subdecks => subdecks;

    /// <summary>Gets notes assigned directly to this deck.</summary>
    public IReadOnlyList<AnkiNote> Notes => notes;

    /// <summary>Adds a direct child deck.</summary>
    /// <param name="name">The child's local name segment.</param>
    /// <returns>The created child.</returns>
    public AnkiDeck AddSubdeck(string name)
    {
        if (subdecks.Any(deck => string.Equals(deck.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Subdeck '{name}' already exists.", nameof(name));
        }

        var deck = new AnkiDeck(name);
        subdecks.Add(deck);
        return deck;
    }

    /// <summary>Adds a note and generates its cards using safe new-card scheduling.</summary>
    /// <param name="noteType">The fully configured note type.</param>
    /// <param name="fields">Values keyed by field name.</param>
    /// <param name="tags">Optional note tags.</param>
    /// <param name="guid">An optional stable Anki GUID.</param>
    /// <returns>The created note.</returns>
    public AnkiNote AddNote(AnkiNoteType noteType, IReadOnlyDictionary<string, string> fields, IEnumerable<string>? tags = null, string? guid = null)
    {
        ArgumentNullException.ThrowIfNull(noteType);
        ArgumentNullException.ThrowIfNull(fields);
        var note = new AnkiNote(noteType, fields, tags, guid: guid);
        note.GenerateCards(Id);
        notes.Add(note);
        return note;
    }

    /// <summary>Removes a note and its generated cards from this deck.</summary>
    /// <param name="note">The note to remove.</param>
    /// <returns><see langword="true"/> when the note was owned by this deck.</returns>
    public bool RemoveNote(AnkiNote note) => notes.Remove(note);

    /// <summary>Enumerates this deck followed by every descendant in stable insertion order.</summary>
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
public sealed partial class AnkiNote
{
    private readonly Dictionary<string, string> fields;
    private readonly HashSet<string> tags;
    private readonly List<AnkiCard> cards = [];

    /// <summary>Initializes a note while preserving its stable identity.</summary>
    public AnkiNote(AnkiNoteType noteType, IReadOnlyDictionary<string, string> fields, IEnumerable<string>? tags = null, long? id = null, string? guid = null)
    {
        NoteType = noteType ?? throw new ArgumentNullException(nameof(noteType));
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

        this.tags = new HashSet<string>(tags ?? [], StringComparer.Ordinal);
    }

    /// <summary>Gets the stable numeric note identifier.</summary>
    public long Id { get; }

    /// <summary>Gets the stable import identity used by Anki.</summary>
    public string Guid { get; }

    /// <summary>Gets the note type that defines field order and card generation.</summary>
    public AnkiNoteType NoteType { get; }

    /// <summary>Gets field values by name.</summary>
    public IReadOnlyDictionary<string, string> Fields => fields;

    /// <summary>Gets tags in ordinal sorted order for deterministic serialization.</summary>
    public IReadOnlyCollection<string> Tags => tags.Order(StringComparer.Ordinal).ToArray();

    /// <summary>Gets cards currently generated for the note.</summary>
    public IReadOnlyList<AnkiCard> Cards => cards;

    /// <summary>Replaces a defined field value.</summary>
    public void SetField(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!fields.ContainsKey(name))
        {
            throw new ArgumentException($"Field '{name}' is not defined by note type '{NoteType.Name}'.", nameof(name));
        }

        fields[name] = value;
    }

    /// <summary>Adds a non-empty note tag.</summary>
    public void AddTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        tags.Add(tag);
    }

    /// <summary>Removes a note tag using ordinal comparison.</summary>
    public bool RemoveTag(string tag) => tags.Remove(tag);

    internal void GenerateCards(long deckId)
    {
        cards.Clear();
        if (NoteType.Kind == AnkiNoteTypeKind.Cloze)
        {
            fields.TryGetValue("Text", out var text);
            var indexes = ClozePattern().Matches(text ?? string.Empty)
                .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .Where(index => index > 0)
                .Distinct()
                .Order()
                .ToArray();
            cards.AddRange(indexes.Select(index => new AnkiCard(AnkiId.New(), Id, deckId, index - 1, AnkiScheduling.New)));
            return;
        }

        for (var ordinal = 0; ordinal < NoteType.Templates.Count; ordinal++)
        {
            cards.Add(new AnkiCard(AnkiId.New(), Id, deckId, ordinal, AnkiScheduling.New));
        }
    }

    [GeneratedRegex(@"\{\{c(\d+)::", RegexOptions.CultureInvariant)]
    private static partial Regex ClozePattern();
}

/// <summary>Represents a study prompt generated from a note template.</summary>
public sealed class AnkiCard
{
    /// <summary>Initializes a card for advanced import and adapter scenarios.</summary>
    public AnkiCard(long id, long noteId, long deckId, int templateOrdinal, AnkiScheduling scheduling)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(templateOrdinal);

        Id = id;
        NoteId = noteId;
        DeckId = deckId;
        TemplateOrdinal = templateOrdinal;
        Scheduling = scheduling ?? throw new ArgumentNullException(nameof(scheduling));
    }

    /// <summary>Gets the stable numeric card identifier.</summary>
    public long Id { get; }

    /// <summary>Gets the owning note identifier.</summary>
    public long NoteId { get; }

    /// <summary>Gets or sets the deck in which this card appears.</summary>
    public long DeckId { get; set; }

    /// <summary>Gets the template ordinal, or zero-based cloze index.</summary>
    public int TemplateOrdinal { get; }

    /// <summary>Gets or sets validated scheduler state.</summary>
    public AnkiScheduling Scheduling { get; set; }

    /// <summary>Gets or sets Anki's low three-bit color flag value.</summary>
    public int Flag { get; set; }

    /// <summary>Gets review-history records retained for round trips.</summary>
    public IList<AnkiReviewLog> ReviewHistory { get; } = new List<AnkiReviewLog>();
}
