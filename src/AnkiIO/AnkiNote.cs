using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace AnkiIO;

/// <summary>Stores one fact or item of knowledge and owns the cards generated from it.</summary>
/// <remarks>
/// Notes and cards are deliberately different: editing a note's field changes what all of its sibling cards render, while
/// scheduling and color flags remain card-specific. Create ordinary notes through <see cref="AnkiDeck.AddNote"/> or the
/// Basic/Cloze helpers so the note is attached and cards are generated. The public constructor exists for adapters and
/// creates a detached note with an empty <see cref="Cards"/> collection.
///
/// <para>
/// Field values may contain HTML and Anki template/media references; AnkiIO stores them verbatim and does not sanitize or
/// render them. Instances are mutable and not safe for concurrent mutation.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var deck = new AnkiDeck("German");
/// var note = deck.AddBasicAndReversedNote("gehen", "to go", tags: ["verb"]);
/// note.SetField("Back", "to go; to walk"); // both sibling cards use the new value
/// note.AddTag("a1");
/// </code>
/// </example>
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
    /// <param name="noteType">A completely configured note type, retained by reference and frozen on success.</param>
    /// <param name="fields">Field values keyed by exact, ordinal field name. Missing defined fields become empty strings.</param>
    /// <param name="tags">
    /// Optional non-empty tags without whitespace; duplicates are collapsed using ordinal comparison. The whitespace
    /// restriction ensures tags can be represented losslessly by legacy Anki package storage.
    /// </param>
    /// <param name="id">An optional stable numeric note identifier, or <see langword="null"/> to generate one.</param>
    /// <param name="guid">An optional stable Anki import GUID, or <see langword="null"/> or blank to generate one.</param>
    /// <remarks>
    /// This constructor does not generate cards or attach the note to a deck. It is useful when an importer will restore
    /// cards separately. After all arguments have been validated it freezes <paramref name="noteType"/> against later field,
    /// template, and CSS changes. Prefer <see cref="AnkiDeck.AddNote"/> for authored decks.
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

    /// <summary>Gets the persisted numeric identity shared by every card generated from this note.</summary>
    /// <value>The caller-supplied ID, or a process-generated positive ID.</value>
    /// <remarks>Preserve imported IDs for round trips; explicit IDs must be unique across the exported graph.</remarks>
    public long Id { get; }

    /// <summary>Gets Anki's stable text identity used to match the same note during import/update workflows.</summary>
    /// <value>The caller-supplied GUID, or a generated ten-character value for new content.</value>
    /// <remarks>
    /// This is not necessarily a <see cref="System.Guid"/> value. Preserve a source GUID when you expect a later import to
    /// update rather than duplicate a note; generate a new one for a genuinely distinct note.
    /// </remarks>
    public string Guid { get; }

    /// <summary>Gets the shared model that defines field order, rendering templates, CSS, and card generation.</summary>
    /// <value>The same instance supplied to the constructor, frozen against structural changes when this note was created.</value>
    public AnkiNoteType NoteType { get; }

    /// <summary>Gets this note's values keyed by the exact names in <see cref="AnkiNoteType.Fields"/>.</summary>
    /// <value>A live, non-castable read-only dictionary; use <see cref="SetField"/> to change a value safely.</value>
    /// <remarks>
    /// Every defined field is present, including fields omitted at construction (stored as empty strings). Values may be
    /// HTML and may refer to registered media by filename. Enumeration follows note-type field order only where the chosen
    /// dictionary/runtime preserves insertion order; use <see cref="AnkiNoteType.Fields"/> when order is semantically required.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Fields => fieldsView;

    /// <summary>Gets note-level labels used for organization and search in Anki.</summary>
    /// <value>A newly allocated, case-sensitive, ordinal-sorted read-only snapshot.</value>
    /// <remarks>
    /// Tags apply to the note and therefore to all sibling cards. Use <see cref="AnkiCard.Flag"/> for a single-card marker.
    /// AnkiIO rejects whitespace inside one tag because legacy Anki storage separates tags with spaces. Hierarchical tag
    /// text such as <c>language::german</c> is allowed.
    /// </remarks>
    public IReadOnlyCollection<string> Tags => Array.AsReadOnly(tags.Order(StringComparer.Ordinal).ToArray());

    /// <summary>Gets the study prompts currently generated for this note.</summary>
    /// <value>A live, non-castable read-only view in template or cloze-index order.</value>
    /// <remarks>
    /// A detached note constructed directly starts empty. <see cref="AnkiDeck.AddNote"/> populates standard cards from
    /// templates or Cloze cards from markers. Modify scheduling/flags on the returned cards; do not try to add cards through
    /// this view.
    /// </remarks>
    public IReadOnlyList<AnkiCard> Cards => cardsView;

    /// <summary>Replaces a defined field value.</summary>
    /// <param name="name">The exact, case-sensitive field name.</param>
    /// <param name="value">The replacement value, including any Anki HTML or media/template markup, stored verbatim.</param>
    /// <remarks>
    /// For standard notes, every sibling card immediately renders the new value and card identity/scheduling are unchanged.
    /// Changing a generated Cloze note's <c>Text</c> also reconciles <see cref="Cards"/> with distinct positive indexes.
    /// Cards for indexes that remain keep their ID, scheduling, flag, and history; removed indexes lose their cards and new
    /// indexes receive safe <see cref="AnkiScheduling.New"/> state. Malformed or overflowing cloze indexes are rejected before
    /// either the field or cards are changed.
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

    /// <summary>Adds a note-level search/organization tag if it is not already present.</summary>
    /// <param name="tag">The case-sensitive tag text without whitespace. Existing identical tags are left unchanged.</param>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is blank or contains whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    public void AddTag(string tag)
    {
        ValidateTag(tag, nameof(tag));
        tags.Add(tag);
    }

    /// <summary>Removes one exact, case-sensitive note tag.</summary>
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
