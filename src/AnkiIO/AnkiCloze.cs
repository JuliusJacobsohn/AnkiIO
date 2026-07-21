using System.Globalization;

namespace AnkiIO;

/// <summary>Builds conservative Anki cloze-deletion markup for a Cloze note's <c>Text</c> field.</summary>
/// <remarks>
/// A cloze deletion hides part of a sentence during review. Deletions sharing an index appear on one card; different
/// positive indexes create separate cards. This helper deliberately supports the common, non-nested form only so user
/// content cannot accidentally terminate the marker. It returns markup text and neither HTML-escapes nor sanitizes it.
///
/// <para>
/// Use <see cref="AnkiDeck.AddClozeNote"/> for the matching conventional note type. Advanced Anki constructs—nested
/// clozes, template markup inside an answer, or content containing <c>{{</c>, <c>::</c>, or <c>}}</c>—must be authored as
/// trusted raw field text with the low-level <see cref="AnkiDeck.AddNote"/> API and tested in the target Anki version.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var deck = new AnkiDeck("Biology");
/// string organelle = AnkiCloze.Wrap("mitochondria", index: 1, hint: "organelle");
/// string role = AnkiCloze.Wrap("ATP", index: 2);
/// deck.AddClozeNote($"{organelle} produce {role}.", tags: ["cell-biology"]);
/// // Two cards are generated: one for c1 and one for c2.
/// </code>
/// </example>
public static class AnkiCloze
{
    /// <summary>Wraps answer text in Anki cloze-deletion markup.</summary>
    /// <param name="text">The answer hidden while the card is shown.</param>
    /// <param name="index">
    /// The positive cloze index. Deletions with the same index appear on one card; different indexes create separate cards.
    /// </param>
    /// <param name="hint">An optional hint displayed in place of the hidden answer.</param>
    /// <returns>
    /// Cloze markup in the form <c>{{c1::answer}}</c>, or <c>{{c1::answer::hint}}</c> when a hint is supplied. The original
    /// answer and hint text are otherwise preserved exactly.
    /// </returns>
    /// <remarks>
    /// The returned value is markup rather than HTML-escaped text. To prevent ambiguous or malformed output, answer text
    /// and hints containing the structural delimiters <c>{{</c>, <c>::</c>, or <c>}}</c> are rejected. Empty answer text is
    /// rejected; an empty hint is allowed and is emitted explicitly. The index is not capped at Anki's UI conventions, but
    /// it must fit a positive <see cref="int"/> and should remain reasonably small for interoperable decks.
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
