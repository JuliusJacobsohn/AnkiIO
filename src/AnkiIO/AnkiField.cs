namespace AnkiIO;

/// <summary>Configures one named input field in an <see cref="AnkiNoteType"/>.</summary>
/// <param name="Name">
/// The field name used as the exact key in <see cref="AnkiNote.Fields"/> and in template expressions such as
/// <c>{{German}}</c>. Names must be non-blank and unique within a note type without regard to case.
/// </param>
/// <param name="IsRightToLeft">
/// <see langword="true"/> to request right-to-left input in compatible Anki editors. This is an editor hint; it does not
/// add <c>direction: rtl</c> to card CSS.
/// </param>
/// <param name="IsSticky">
/// <see langword="true"/> to ask compatible Anki editors to carry the previous value into the next new note. This does
/// not copy values when notes are created through AnkiIO.
/// </param>
/// <param name="Font">
/// The non-blank font-family preference for Anki's note editor. It does not select the font on rendered study cards.
/// </param>
/// <param name="FontSize">
/// The positive editor font size in pixels. It does not change the card's rendered font size.
/// </param>
/// <remarks>
/// Field order is significant: Anki package formats store note values positionally, and <see cref="AnkiNoteType.Fields"/>
/// defines that order. Add configured fields before creating the first note, because the note type freezes on first use.
///
/// <para>
/// <see cref="Font"/>, <see cref="FontSize"/>, <see cref="IsRightToLeft"/>, and <see cref="IsSticky"/> describe the note
/// editor only. Put presentation rules in <see cref="AnkiNoteType.Css"/> or the card template. Constructing this immutable
/// record does not validate its arguments; <see cref="AnkiNoteType.AddConfiguredField"/> validates them when the field is
/// attached, which allows ordinary <c>with</c> expressions while keeping invalid definitions out of a note type.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var field = new AnkiField("Arabic", IsRightToLeft: true, Font: "Noto Sans Arabic", FontSize: 24);
/// var type = new AnkiNoteType("Vocabulary").AddConfiguredField(field);
/// </code>
/// </example>
public sealed record AnkiField(
    string Name,
    bool IsRightToLeft = false,
    bool IsSticky = false,
    string Font = "Arial",
    int FontSize = 20)
{
    /// <summary>Gets the exact field name used by note values and template references.</summary>
    /// <value>The name supplied to the primary constructor; validated when added to a note type.</value>
    public string Name { get; init; } = Name;

    /// <summary>Gets whether compatible Anki editors should use right-to-left input for this field.</summary>
    /// <value><see langword="true"/> when right-to-left editor input is requested; otherwise, <see langword="false"/>.</value>
    public bool IsRightToLeft { get; init; } = IsRightToLeft;

    /// <summary>Gets whether compatible Anki editors may retain this field's value for the next manually entered note.</summary>
    /// <value><see langword="true"/> when the field is sticky; otherwise, <see langword="false"/>.</value>
    public bool IsSticky { get; init; } = IsSticky;

    /// <summary>Gets the note-editor font-family preference, not the rendered card font.</summary>
    /// <value>The font name supplied to the primary constructor, or <c>Arial</c> by default.</value>
    public string Font { get; init; } = Font;

    /// <summary>Gets the note-editor font-size preference in pixels, not the rendered card size.</summary>
    /// <value>The size supplied to the primary constructor, or 20 by default.</value>
    public int FontSize { get; init; } = FontSize;
}
