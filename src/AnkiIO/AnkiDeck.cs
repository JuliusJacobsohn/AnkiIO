using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace AnkiIO;

/// <summary>Builds one named deck hierarchy and acts as the root for validation and export.</summary>
/// <remarks>
/// AnkiIO stores each hierarchy segment separately: create <c>Languages::German</c> by constructing <c>Languages</c> and
/// calling <see cref="AddSubdeck"/> with <c>German</c>. Notes belong to the deck on which they were added, while individual
/// cards may reference another deck through <see cref="AnkiCard.DeckId"/>. Package and validation operations starting at a
/// root include all descendants.
///
/// <para>
/// The object graph is intentionally mutable for build/import workflows but is not thread-safe. Complete custom note
/// types before adding notes, register every referenced media filename, and validate before export. AnkiIO creates package
/// files; it never needs to write a live Anki profile.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var languages = new AnkiDeck("Languages");
/// var german = languages.AddSubdeck("German");
/// german.AddBasicNote("Haus", "house", tags: ["noun"]);
/// german.Media.AddBytes("house.svg", svgBytes);
///
/// await AnkiPackageWriter.WriteAsync(languages, "Languages.apkg");
/// </code>
/// </example>
public sealed class AnkiDeck
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
    /// <remarks>
    /// The deck starts with no notes, subdecks, or media. The public constructor creates a hierarchy root; child decks
    /// created with <see cref="AddSubdeck"/> share the root's conventional-note-type cache so helper-created notes reuse one
    /// Basic, reversed, or Cloze model instead of duplicating models in Anki.
    /// </remarks>
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

    /// <summary>Gets the persisted identity used by cards and package metadata to refer to this deck.</summary>
    /// <value>The caller-supplied ID, or a process-generated positive ID.</value>
    /// <remarks>
    /// Preserve an imported ID when updating the same logical deck. Explicit IDs must be unique across the entire exported
    /// hierarchy; use <see cref="AnkiId.FromStableValue"/> for deterministic external mappings.
    /// </remarks>
    public long Id { get; }

    /// <summary>Gets this deck's local display-name segment, not its full hierarchy path.</summary>
    /// <value>A non-empty segment that never contains Anki's <c>::</c> hierarchy separator.</value>
    public string Name { get; }

    /// <summary>Gets or sets the description Anki may show on the deck overview screen.</summary>
    /// <value>HTML stored verbatim; the default is an empty string.</value>
    /// <remarks>
    /// AnkiIO does not sanitize, render, or execute this content. Do not insert untrusted HTML without applying the content
    /// policy appropriate to your application. Legacy APKG and native JSON preserve the modeled value; other adapters may
    /// omit it.
    /// </remarks>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets application-defined string metadata for AnkiIO native-JSON round trips.</summary>
    /// <value>
    /// A live, mutable, case-sensitive dictionary. Legacy APKG and CrowdAnki-inspired writers do not preserve these values.
    /// </value>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets unknown native-JSON deck properties retained without interpretation.</summary>
    /// <value>
    /// A live, mutable, case-sensitive dictionary of cloned JSON values. Preservation is limited to unknown properties on
    /// deck objects in the native AnkiIO JSON format; it is not a general APKG/protobuf preservation mechanism.
    /// </value>
    public IDictionary<string, JsonElement> UnknownData { get; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>Gets media filenames and payloads contributed by this deck during package export.</summary>
    /// <value>
    /// A mutable media collection created with the deck. Package writing aggregates media from this deck and every
    /// descendant. A field such as <c>&lt;img src="house.png"&gt;</c> is only text until a payload named
    /// <c>house.png</c> is registered. Registrations are not copied between parent and child decks.
    /// </value>
    public AnkiMediaCollection Media { get; }

    /// <summary>Gets only the direct children created below this deck.</summary>
    /// <value>A live, non-castable read-only view in insertion order; use <see cref="Traverse"/> for all descendants.</value>
    public IReadOnlyList<AnkiDeck> Subdecks => subdecksView;

    /// <summary>Gets notes assigned directly to this deck.</summary>
    /// <value>A live, non-castable read-only view in insertion order. Descendant notes are not included.</value>
    /// <remarks>Add through a convenience method or <see cref="AddNote"/> so note-type freezing and card generation occur.</remarks>
    public IReadOnlyList<AnkiNote> Notes => notesView;

    /// <summary>Creates and adds a direct child deck.</summary>
    /// <param name="name">A non-empty local name segment without Anki's <c>::</c> hierarchy separator.</param>
    /// <param name="id">
    /// An optional stable numeric deck ID for repeatable imports; when omitted, <see cref="AnkiId.New"/> generates one.
    /// </param>
    /// <returns>The newly created child deck, ready for notes or further nested decks.</returns>
    /// <remarks>
    /// Sibling names are compared without regard to case, matching Anki's practical hierarchy behavior. The returned child
    /// shares conventional Basic/reversed/Cloze definitions with the root. Supplying <c>German::Verbs</c> is invalid; call
    /// <c>AddSubdeck("German").AddSubdeck("Verbs")</c> instead.
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
    /// <param name="noteType">The fully configured definition whose fields/templates will be frozen by this operation.</param>
    /// <param name="fields">
    /// Values keyed by exact, case-sensitive field name. Omitted defined fields become empty strings; unknown keys fail.
    /// Values may contain Anki HTML/template text and are not sanitized.
    /// </param>
    /// <param name="tags">Optional case-sensitive note tags. Tags cannot be blank or contain whitespace.</param>
    /// <param name="guid">An optional stable Anki import GUID used to recognize the note across imports.</param>
    /// <param name="id">An optional persisted numeric note ID; omit it for newly authored content.</param>
    /// <returns>The attached note with cards generated immediately for its templates or current cloze indexes.</returns>
    /// <remarks>
    /// The note retains <paramref name="noteType"/> by reference and freezes it after argument validation, preventing later
    /// field/template/CSS changes from invalidating existing notes. Standard types produce one card per template. Cloze
    /// types produce one card per distinct positive marker in the <c>Text</c> field. An exact conventional Basic, reversed,
    /// or Cloze definition is reused by later helper calls in this hierarchy.
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
    /// <remarks>
    /// Removal is by object identity/equality from this direct list only; subdecks are not searched. The detached note and
    /// cards remain usable in memory, and media registrations or note-type definitions are not removed automatically.
    /// </remarks>
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

    /// <summary>Enumerates the complete hierarchy in the same deterministic order used by writers.</summary>
    /// <returns>A lazy depth-first sequence: this deck, then each child subtree in insertion order.</returns>
    /// <remarks>
    /// The sequence is live, not a snapshot. Do not add subdecks while enumerating it. Use it when aggregating notes or media
    /// across a hierarchy; <see cref="Notes"/> and <see cref="Subdecks"/> deliberately expose only direct ownership.
    /// </remarks>
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

    /// <summary>Adds a conventional Basic note that generates one front-to-back card.</summary>
    /// <param name="front">The card question. Anki HTML and media references are preserved as supplied.</param>
    /// <param name="back">The card answer. Anki HTML and media references are preserved as supplied.</param>
    /// <param name="tags">
    /// Optional case-sensitive note tags without whitespace. Exact duplicates are collapsed using ordinal comparison.
    /// </param>
    /// <param name="guid">An optional stable Anki GUID for repeatable imports; when omitted, a new GUID is generated.</param>
    /// <param name="id">An optional stable numeric note ID; when omitted, a new ID is generated.</param>
    /// <returns>The added note, including its single newly generated card.</returns>
    /// <remarks>
    /// Unless an exact conventional definition has already been observed, the first call in a deck hierarchy creates a
    /// <c>Basic</c> note type. Later calls on the root or any subdeck reuse it, including after a supported import round trip.
    /// Creating the first note freezes the cached type. Use the low-level overload when custom fields/templates/CSS are
    /// required; a conventional helper type cannot be modified after use.
    /// </remarks>
    /// <example>
    /// <code>
    /// var deck = new AnkiDeck("German");
    /// deck.AddBasicNote("Haus", "house", tags: ["noun"]);
    /// await AnkiPackageWriter.WriteAsync(deck, "German.apkg");
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="front"/> or <paramref name="back"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tags"/> contains a blank or whitespace-containing tag.</exception>
    public AnkiNote AddBasicNote(
        string front,
        string back,
        IEnumerable<string>? tags = null,
        string? guid = null,
        long? id = null)
    {
        ArgumentNullException.ThrowIfNull(front);
        ArgumentNullException.ThrowIfNull(back);

        return AddNote(
            conventionalNoteTypes.Basic ??= AnkiNoteTypes.CreateBasic(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Front"] = front,
                ["Back"] = back,
            },
            tags,
            guid,
            id);
    }

    /// <summary>Adds a conventional Basic (and reversed) note that generates front-to-back and back-to-front cards.</summary>
    /// <param name="front">The first side of the note. Anki HTML and media references are preserved as supplied.</param>
    /// <param name="back">The second side of the note. Anki HTML and media references are preserved as supplied.</param>
    /// <param name="tags">
    /// Optional case-sensitive note tags without whitespace. Exact duplicates are collapsed using ordinal comparison.
    /// </param>
    /// <param name="guid">An optional stable Anki GUID for repeatable imports; when omitted, a new GUID is generated.</param>
    /// <param name="id">An optional stable numeric note ID; when omitted, a new ID is generated.</param>
    /// <returns>The added note, including its two newly generated cards in front-to-back then back-to-front order.</returns>
    /// <remarks>
    /// Unless an exact conventional definition has already been observed, the first call in a deck hierarchy creates a
    /// <c>Basic (and reversed card)</c> note type. Later calls on the root or any subdeck reuse the same frozen definition.
    /// This helper always creates both directions; for a reverse card controlled by an extra field, use
    /// <see cref="AddNote(AnkiNoteType, IReadOnlyDictionary{string, string}, IEnumerable{string}?, string?, long?)"/>
    /// for custom templates or conditional reverse-card generation.
    /// </remarks>
    /// <example>
    /// <code>
    /// var deck = new AnkiDeck("Vocabulary");
    /// deck.AddBasicAndReversedNote("gehen", "to go");
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="front"/> or <paramref name="back"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tags"/> contains a blank or whitespace-containing tag.</exception>
    public AnkiNote AddBasicAndReversedNote(
        string front,
        string back,
        IEnumerable<string>? tags = null,
        string? guid = null,
        long? id = null)
    {
        ArgumentNullException.ThrowIfNull(front);
        ArgumentNullException.ThrowIfNull(back);

        return AddNote(
            conventionalNoteTypes.BasicAndReversed ??= AnkiNoteTypes.CreateBasicAndReversed(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Front"] = front,
                ["Back"] = back,
            },
            tags,
            guid,
            id);
    }

    /// <summary>Adds a conventional Cloze note and generates one card for each distinct positive cloze index.</summary>
    /// <param name="text">
    /// The cloze-formatted main text, for example <c>"The capital is {{c1::Berlin}}."</c>. Use
    /// <see cref="AnkiCloze.Wrap(string, int, string?)"/> to construct individual deletions safely.
    /// </param>
    /// <param name="extra">Optional supporting HTML shown on the answer side.</param>
    /// <param name="tags">
    /// Optional case-sensitive note tags without whitespace. Exact duplicates are collapsed using ordinal comparison.
    /// </param>
    /// <param name="guid">An optional stable Anki GUID for repeatable imports; when omitted, a new GUID is generated.</param>
    /// <param name="id">An optional stable numeric note ID; when omitted, a new ID is generated.</param>
    /// <returns>The added note and its newly generated cloze cards, ordered by cloze index.</returns>
    /// <remarks>
    /// Repeating the same cloze index produces one card containing all deletions with that index. Different indexes
    /// produce separate cards. The conventional frozen Cloze note type is shared by all helper calls in this deck hierarchy,
    /// including when an exact conventional definition was reconstructed by a supported importer.
    /// This convenience method accepts balanced, non-nested deletions with an optional single hint. It rejects empty
    /// answers, non-positive or unrepresentable indexes, nested braces, and additional <c>::</c> separators. Prefer
    /// <see cref="AnkiCloze.Wrap(string, int, string?)"/> when constructing deletions programmatically.
    /// Use <see cref="AddNote(AnkiNoteType, IReadOnlyDictionary{string, string}, IEnumerable{string}?, string?, long?)"/>
    /// to create an initially empty cloze note or to use advanced nested syntax, custom fields, templates, or CSS.
    /// </remarks>
    /// <example>
    /// <code>
    /// var deck = new AnkiDeck("Geography");
    /// var answer = AnkiCloze.Wrap("Berlin", hint: "city");
    /// deck.AddClozeNote($"Germany's capital is {answer}.");
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="extra"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> contains no valid deletion, or its simple cloze markup is empty, unbalanced, nested,
    /// ambiguous, non-positive, or outside the range supported by <see cref="int"/>; or <paramref name="tags"/> contains a
    /// blank or whitespace-containing tag.
    /// </exception>
    public AnkiNote AddClozeNote(
        string text,
        string extra = "",
        IEnumerable<string>? tags = null,
        string? guid = null,
        long? id = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(extra);
        ValidateSimpleClozeText(text);

        return AddNote(
            conventionalNoteTypes.Cloze ??= AnkiNoteTypes.CreateCloze(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Text"] = text,
                ["Extra"] = extra,
            },
            tags,
            guid,
            id);
    }

    private static void ValidateSimpleClozeText(string text)
    {
        const string marker = "{{c";
        var searchIndex = 0;
        var foundDeletion = false;

        while (text.IndexOf(marker, searchIndex, StringComparison.Ordinal) is var markerIndex && markerIndex >= 0)
        {
            var indexStart = markerIndex + marker.Length;
            if (indexStart >= text.Length || text[indexStart] is < '0' or > '9')
            {
                throw new ArgumentException(
                    "Cloze markers must place a positive numeric index after '{{c', such as '{{c1::answer}}'.",
                    nameof(text));
            }

            var contentSeparator = indexStart;
            while (contentSeparator < text.Length && text[contentSeparator] is >= '0' and <= '9')
            {
                contentSeparator++;
            }

            if (contentSeparator + 1 >= text.Length
                || text[contentSeparator] != ':'
                || text[contentSeparator + 1] != ':')
            {
                throw new ArgumentException("Cloze indexes must be followed by the '::' content separator.", nameof(text));
            }

            var indexText = text.AsSpan(indexStart, contentSeparator - indexStart);
            if (indexText[0] == '0'
                || !int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index < 1)
            {
                throw new ArgumentException("Cloze indexes must be positive integers representable by System.Int32.", nameof(text));
            }

            var contentStart = contentSeparator + 2;
            var closingDelimiter = text.IndexOf("}}", contentStart, StringComparison.Ordinal);
            if (closingDelimiter < 0)
            {
                throw new ArgumentException("Cloze deletions must end with '}}'.", nameof(text));
            }

            var content = text[contentStart..closingDelimiter];
            if (content.Contains("{{", StringComparison.Ordinal))
            {
                throw new ArgumentException("Nested cloze or template markup requires the low-level AddNote API.", nameof(text));
            }

            var hintSeparator = content.IndexOf("::", StringComparison.Ordinal);
            var answer = hintSeparator < 0 ? content : content[..hintSeparator];
            if (answer.Length == 0)
            {
                throw new ArgumentException("Cloze answer text cannot be empty.", nameof(text));
            }

            if (hintSeparator >= 0
                && content.IndexOf("::", hintSeparator + 2, StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException("Simple cloze markup can contain at most one hint separator.", nameof(text));
            }

            foundDeletion = true;
            searchIndex = closingDelimiter + 2;
        }

        if (!foundDeletion)
        {
            throw new ArgumentException("Cloze text must contain at least one positive deletion such as '{{c1::answer}}'.", nameof(text));
        }
    }

}
