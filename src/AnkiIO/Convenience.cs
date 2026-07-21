using System.Globalization;

namespace AnkiIO;

public sealed partial class AnkiDeck
{
    /// <summary>Adds a conventional Basic note that generates one front-to-back card.</summary>
    /// <param name="front">The card question. Anki HTML and media references are preserved as supplied.</param>
    /// <param name="back">The card answer. Anki HTML and media references are preserved as supplied.</param>
    /// <param name="tags">Optional tags to attach to the note. Duplicate tags are collapsed using ordinal comparison.</param>
    /// <param name="guid">An optional stable Anki GUID for repeatable imports; when omitted, a new GUID is generated.</param>
    /// <param name="id">An optional stable numeric note ID; when omitted, a new ID is generated.</param>
    /// <returns>The added note, including its single newly generated card.</returns>
    /// <remarks>
    /// Unless an exact conventional definition has already been observed, the first call in a deck hierarchy creates a
    /// <c>Basic</c> note type. Later calls on the root or any subdeck reuse it, including after a supported import round trip.
    /// The cached type remains mutable through <see cref="AnkiNote.NoteType"/>; do not modify it unless every helper-created
    /// note in the hierarchy is intended to share the change.
    /// Use <see cref="AddNote(AnkiNoteType, IReadOnlyDictionary{string, string}, IEnumerable{string}?, string?, long?)"/>
    /// when custom fields, templates, CSS, or a caller-owned note type are required.
    /// </remarks>
    /// <example>
    /// <code>
    /// var deck = new AnkiDeck("German");
    /// deck.AddBasicNote("Haus", "house", tags: ["noun"]);
    /// await AnkiPackageWriter.WriteAsync(deck, "German.apkg");
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="front"/> or <paramref name="back"/> is <see langword="null"/>.</exception>
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
    /// <param name="tags">Optional tags to attach to the note. Duplicate tags are collapsed using ordinal comparison.</param>
    /// <param name="guid">An optional stable Anki GUID for repeatable imports; when omitted, a new GUID is generated.</param>
    /// <param name="id">An optional stable numeric note ID; when omitted, a new ID is generated.</param>
    /// <returns>The added note, including its two newly generated cards in front-to-back then back-to-front order.</returns>
    /// <remarks>
    /// Unless an exact conventional definition has already been observed, the first call in a deck hierarchy creates a
    /// <c>Basic (and reversed card)</c> note type. Later calls on the root or any subdeck reuse it. Use
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
    /// <param name="tags">Optional tags to attach to the note. Duplicate tags are collapsed using ordinal comparison.</param>
    /// <param name="guid">An optional stable Anki GUID for repeatable imports; when omitted, a new GUID is generated.</param>
    /// <param name="id">An optional stable numeric note ID; when omitted, a new ID is generated.</param>
    /// <returns>The added note and its newly generated cloze cards, ordered by cloze index.</returns>
    /// <remarks>
    /// Repeating the same cloze index produces one card containing all deletions with that index. Different indexes
    /// produce separate cards. The conventional Cloze note type is shared by all helper calls in this deck hierarchy,
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
    /// ambiguous, non-positive, or outside the range supported by <see cref="int"/>.
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

    private sealed class ConventionalNoteTypeCache
    {
        public AnkiNoteType? Basic { get; set; }

        public AnkiNoteType? BasicAndReversed { get; set; }

        public AnkiNoteType? Cloze { get; set; }

        public void Observe(AnkiNoteType noteType)
        {
            if (Basic is null && AnkiNoteTypes.IsBasic(noteType))
            {
                Basic = noteType;
            }
            else if (BasicAndReversed is null && AnkiNoteTypes.IsBasicAndReversed(noteType))
            {
                BasicAndReversed = noteType;
            }
            else if (Cloze is null && AnkiNoteTypes.IsCloze(noteType))
            {
                Cloze = noteType;
            }
        }

        public void CopyMissingTo(ConventionalNoteTypeCache destination)
        {
            if (Basic is not null)
            {
                destination.Observe(Basic);
            }

            if (BasicAndReversed is not null)
            {
                destination.Observe(BasicAndReversed);
            }

            if (Cloze is not null)
            {
                destination.Observe(Cloze);
            }
        }
    }
}

/// <summary>Builds valid Anki cloze-deletion markup for use in a Cloze note's <c>Text</c> field.</summary>
public static class AnkiCloze
{
    /// <summary>Wraps answer text in Anki cloze-deletion markup.</summary>
    /// <param name="text">The answer hidden while the card is shown.</param>
    /// <param name="index">
    /// The positive cloze index. Deletions with the same index appear on one card; different indexes create separate cards.
    /// </param>
    /// <param name="hint">An optional hint displayed in place of the hidden answer.</param>
    /// <returns>
    /// Cloze markup in the form <c>{{c1::answer}}</c>, or <c>{{c1::answer::hint}}</c> when a hint is supplied.
    /// </returns>
    /// <remarks>
    /// The returned value is markup rather than HTML-escaped text. To prevent ambiguous or malformed markup, answer text
    /// and hints containing the structural delimiters <c>{{</c>, <c>::</c>, or <c>}}</c> are rejected. Callers needing advanced raw
    /// syntax can construct the <c>Text</c> field directly.
    /// </remarks>
    /// <example>
    /// <code>
    /// string deletion = AnkiCloze.Wrap("mitochondria", index: 1, hint: "organelle");
    /// // {{c1::mitochondria::organelle}}
    /// </code>
    /// </example>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> is empty, or <paramref name="text"/> or <paramref name="hint"/> contains a cloze delimiter.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is less than one.</exception>
    public static string Wrap(string text, int index = 1, string? hint = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        RejectDelimiter(text, nameof(text));
        if (hint is not null)
        {
            RejectDelimiter(hint, nameof(hint));
        }

        var indexText = index.ToString(CultureInfo.InvariantCulture);
        return hint is null
            ? $"{{{{c{indexText}::{text}}}}}"
            : $"{{{{c{indexText}::{text}::{hint}}}}}";
    }

    private static void RejectDelimiter(string value, string parameterName)
    {
        if (value.Contains("{{", StringComparison.Ordinal)
            || value.Contains("::", StringComparison.Ordinal)
            || value.Contains("}}", StringComparison.Ordinal))
        {
            throw new ArgumentException("Cloze content cannot contain the structural delimiters '{{', '::', or '}}'.", parameterName);
        }
    }
}
