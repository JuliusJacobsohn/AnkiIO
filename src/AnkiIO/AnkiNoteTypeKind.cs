namespace AnkiIO;

/// <summary>Specifies how an <see cref="AnkiNoteType"/> turns one note into study cards.</summary>
/// <remarks>
/// Choose the kind when the note type is constructed; it cannot change later. AnkiIO supports the two strategies below and
/// does not run template rendering or Anki's “empty card” checks while deciding which cards to create.
/// </remarks>
public enum AnkiNoteTypeKind
{
    /// <summary>Generates one card for every template in <see cref="AnkiNoteType.Templates"/>, in insertion order.</summary>
    /// <remarks>
    /// Template ordinal zero maps to the first template. Conditional template markup is preserved for Anki to render, but
    /// AnkiIO still creates the card even when the rendered front could be empty.
    /// </remarks>
    Standard,

    /// <summary>Generates one card for every distinct positive <c>{{cN::...}}</c> index in the <c>Text</c> field.</summary>
    /// <remarks>
    /// Repeated <c>c1</c> deletions share one card, while <c>c1</c> and <c>c2</c> produce separate cards with ordinals zero
    /// and one. The model should define a field named <c>Text</c> and a template using <c>{{cloze:Text}}</c>; validation
    /// reports missing structural pieces before output.
    /// </remarks>
    Cloze,
}
