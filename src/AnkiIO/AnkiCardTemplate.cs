namespace AnkiIO;

/// <summary>Defines one study direction by mapping note fields to a card front and back.</summary>
/// <param name="Name">The non-blank name shown in Anki's card-type editor, unique within its note type ignoring case.</param>
/// <param name="QuestionFormat">
/// Anki HTML/template markup for the front, for example <c>{{German}}</c>. The value must be non-null but may be empty.
/// </param>
/// <param name="AnswerFormat">
/// Anki HTML/template markup for the back. <c>{{FrontSide}}</c> reuses the already-rendered front.
/// </param>
/// <param name="BrowserQuestionFormat">
/// Optional markup for Anki's browser question column, or <see langword="null"/> to emit no browser-specific override.
/// </param>
/// <param name="BrowserAnswerFormat">
/// Optional markup for Anki's browser answer column, or <see langword="null"/> to emit no browser-specific override.
/// </param>
/// <remarks>
/// The formats use Anki's template language, not C# interpolation. Common expressions insert a field, conditionally show
/// a section based on a field, reuse <c>{{FrontSide}}</c>, or apply modifiers such as <c>{{type:Field}}</c> and, for Cloze
/// models, <c>{{cloze:Text}}</c>. Values may also contain ordinary HTML and media references.
///
/// <para>
/// AnkiIO stores markup verbatim and never renders, HTML-sanitizes, or executes it. Validation reports unknown field
/// references in the main question/answer formats, but it is not a full Anki template compiler: it does not prove that
/// conditional sections are balanced, that HTML is safe, or that browser-specific formats render. Test custom templates
/// in the Anki version you support.
/// </para>
///
/// <para>
/// A Standard note normally receives one card per template in insertion order. AnkiIO creates that card eagerly and does
/// not evaluate conditional templates to decide whether an empty card should be omitted. A Cloze note instead receives one
/// card per distinct positive cloze index in its <c>Text</c> field; adding more templates does not create more Cloze cards.
/// </para>
///
/// Constructing this immutable record does not validate its arguments. Attach it with
/// <see cref="AnkiNoteType.AddConfiguredTemplate"/> to validate required values and uniqueness before use.
/// </remarks>
/// <example>
/// <code>
/// var template = new AnkiCardTemplate(
///     "Recognition",
///     "{{Word}}",
///     "{{FrontSide}}&lt;hr id=\"answer\"&gt;{{Meaning}}",
///     BrowserQuestionFormat: "{{Word}}",
///     BrowserAnswerFormat: "{{Meaning}}");
/// </code>
/// </example>
public sealed record AnkiCardTemplate(
    string Name,
    string QuestionFormat,
    string AnswerFormat,
    string? BrowserQuestionFormat = null,
    string? BrowserAnswerFormat = null)
{
    /// <summary>Gets the display name shown for this card type in Anki.</summary>
    /// <value>The name supplied to the primary constructor; validated when attached to a note type.</value>
    public string Name { get; init; } = Name;

    /// <summary>Gets the unsanitized Anki HTML/template markup used for the card front.</summary>
    /// <value>The question format supplied to the primary constructor.</value>
    public string QuestionFormat { get; init; } = QuestionFormat;

    /// <summary>Gets the unsanitized Anki HTML/template markup used for the card back.</summary>
    /// <value>The answer format supplied to the primary constructor.</value>
    public string AnswerFormat { get; init; } = AnswerFormat;

    /// <summary>Gets the optional markup used for the question column in Anki's card browser.</summary>
    /// <value>The browser question format, or <see langword="null"/> when no browser-specific override is emitted.</value>
    public string? BrowserQuestionFormat { get; init; } = BrowserQuestionFormat;

    /// <summary>Gets the optional markup used for the answer column in Anki's card browser.</summary>
    /// <value>The browser answer format, or <see langword="null"/> when no browser-specific override is emitted.</value>
    public string? BrowserAnswerFormat { get; init; } = BrowserAnswerFormat;
}
