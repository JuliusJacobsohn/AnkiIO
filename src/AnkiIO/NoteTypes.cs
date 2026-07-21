namespace AnkiIO;

/// <summary>Specifies the strategy an <see cref="AnkiNoteType"/> uses to generate cards from a note.</summary>
/// <remarks>
/// Standard note types generate cards from their ordered templates. Cloze note types derive cards from distinct
/// positive cloze indexes in the note's <c>Text</c> field.
/// </remarks>
public enum AnkiNoteTypeKind
{
    /// <summary>Generates one card for every template in <see cref="AnkiNoteType.Templates"/>.</summary>
    Standard,

    /// <summary>Generates one card for every distinct positive <c>{{cN::...}}</c> index in the <c>Text</c> field.</summary>
    Cloze,
}

/// <summary>Describes one ordered field in an <see cref="AnkiNoteType"/>.</summary>
/// <param name="Name">The non-empty field name, unique within its note type without regard to case.</param>
/// <param name="IsRightToLeft"><see langword="true"/> to request right-to-left input in compatible Anki editors.</param>
/// <param name="IsSticky"><see langword="true"/> to let compatible Anki editors retain the value for the next note.</param>
/// <param name="Font">The non-empty editor font-family preference; this does not change rendered card CSS.</param>
/// <param name="FontSize">The positive editor font size in pixels; this does not change rendered card CSS.</param>
/// <remarks>
/// Field order determines the storage order used by Anki package formats. Constructing the record captures metadata;
/// <see cref="AnkiNoteType.AddConfiguredField"/> validates it when attaching it to a note type.
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
    /// <summary>Gets the field name used by note values and template references.</summary>
    /// <value>The name supplied to the primary constructor.</value>
    public string Name { get; init; } = Name;

    /// <summary>Gets whether compatible Anki editors should use right-to-left input for this field.</summary>
    /// <value><see langword="true"/> when right-to-left editor input is requested; otherwise, <see langword="false"/>.</value>
    public bool IsRightToLeft { get; init; } = IsRightToLeft;

    /// <summary>Gets whether compatible Anki editors may retain this field's value for the next note.</summary>
    /// <value><see langword="true"/> when the field is sticky; otherwise, <see langword="false"/>.</value>
    public bool IsSticky { get; init; } = IsSticky;

    /// <summary>Gets the editor font-family preference.</summary>
    /// <value>The font name supplied to the primary constructor, or <c>Arial</c> by default.</value>
    public string Font { get; init; } = Font;

    /// <summary>Gets the editor font-size preference in pixels.</summary>
    /// <value>The size supplied to the primary constructor, or 20 by default.</value>
    public int FontSize { get; init; } = FontSize;
}

/// <summary>Describes how note fields form the front and back of one generated card.</summary>
/// <param name="Name">The non-empty display name, unique within its note type without regard to case.</param>
/// <param name="QuestionFormat">Non-null Anki HTML and template markup for the card front.</param>
/// <param name="AnswerFormat">Non-null Anki HTML and template markup for the card back.</param>
/// <param name="BrowserQuestionFormat">Optional markup used for the question column in Anki's card browser.</param>
/// <param name="BrowserAnswerFormat">Optional markup used for the answer column in Anki's card browser.</param>
/// <remarks>
/// Template markup is preserved rather than interpreted by this record. Field references are checked by
/// <see cref="AnkiValidator.Validate(AnkiDeck)"/> once the note type is used by a note.
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
    /// <summary>Gets the template's display name.</summary>
    /// <value>The name supplied to the primary constructor.</value>
    public string Name { get; init; } = Name;

    /// <summary>Gets the Anki HTML and template markup used for the card front.</summary>
    /// <value>The question format supplied to the primary constructor.</value>
    public string QuestionFormat { get; init; } = QuestionFormat;

    /// <summary>Gets the Anki HTML and template markup used for the card back.</summary>
    /// <value>The answer format supplied to the primary constructor.</value>
    public string AnswerFormat { get; init; } = AnswerFormat;

    /// <summary>Gets the optional markup used for the question column in Anki's card browser.</summary>
    /// <value>The browser question format, or <see langword="null"/> when the standard question format should be used.</value>
    public string? BrowserQuestionFormat { get; init; } = BrowserQuestionFormat;

    /// <summary>Gets the optional markup used for the answer column in Anki's card browser.</summary>
    /// <value>The browser answer format, or <see langword="null"/> when the standard answer format should be used.</value>
    public string? BrowserAnswerFormat { get; init; } = BrowserAnswerFormat;
}

/// <summary>Defines the ordered fields, card templates, and shared CSS for a family of Anki notes.</summary>
/// <remarks>
/// Instances use controlled mutation while being assembled. Notes retain a reference to their note type, so finish
/// configuring an instance before passing it to <see cref="AnkiDeck.AddNote"/>. Field and template names are unique
/// without regard to case, while field lookups in note values use the stored spelling.
/// </remarks>
/// <example>
/// <code>
/// var vocabulary = new AnkiNoteType("Vocabulary")
///     .AddConfiguredField(new AnkiField("Word", FontSize: 24))
///     .AddField("Meaning")
///     .AddConfiguredTemplate(new AnkiCardTemplate("Recognition", "{{Word}}", "{{Meaning}}"));
/// </code>
/// </example>
public sealed class AnkiNoteType
{
    internal const string DefaultCss = ".card { font-family: Arial; font-size: 20px; text-align: center; color: black; background: white; }";

    private readonly List<AnkiField> fields = [];
    private readonly List<AnkiCardTemplate> templates = [];

    /// <summary>Initializes an empty note type.</summary>
    /// <param name="name">The non-empty user-visible note-type name.</param>
    /// <param name="kind">The strategy used to generate cards from notes of this type.</param>
    /// <param name="id">
    /// An optional stable numeric identifier for repeatable imports; when omitted, <see cref="AnkiId.New"/> generates one.
    /// </param>
    /// <remarks>Add all required fields and templates before using the note type to create notes.</remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or consists only of whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public AnkiNoteType(string name, AnkiNoteTypeKind kind = AnkiNoteTypeKind.Standard, long? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Kind = kind;
        Id = id ?? AnkiId.New();
    }

    /// <summary>Gets the stable numeric note-type identifier.</summary>
    /// <value>The caller-supplied identifier, or a generated positive identifier.</value>
    public long Id { get; }

    /// <summary>Gets the user-visible name.</summary>
    /// <value>The name supplied when this instance was constructed.</value>
    public string Name { get; }

    /// <summary>Gets the card-generation strategy.</summary>
    /// <value>The immutable <see cref="AnkiNoteTypeKind"/> selected at construction.</value>
    public AnkiNoteTypeKind Kind { get; }

    /// <summary>Gets or sets CSS shared by every card template in this note type.</summary>
    /// <value>Anki-compatible CSS. The default renders centered black Arial text on a white background.</value>
    /// <remarks>The value is stored verbatim. AnkiIO does not parse, sanitize, or normalize CSS.</remarks>
    public string Css { get; set; } = DefaultCss;

    /// <summary>Gets fields in their storage and display order.</summary>
    /// <value>A read-only view that reflects fields subsequently added to this note type.</value>
    public IReadOnlyList<AnkiField> Fields => fields;

    /// <summary>Gets templates in card-ordinal order.</summary>
    /// <value>A read-only view that reflects templates subsequently added to this note type.</value>
    public IReadOnlyList<AnkiCardTemplate> Templates => templates;

    /// <summary>Adds a uniquely named field at the end of the field order.</summary>
    /// <param name="name">The non-empty field name used by notes and template references.</param>
    /// <returns>This instance for fluent construction.</returns>
    /// <remarks>This overload creates a field with the default editor direction, stickiness, font, and font size.</remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank or duplicates a field name without regard to case.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public AnkiNoteType AddField(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (fields.Any(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Field '{name}' already exists.", nameof(name));
        }

        fields.Add(new AnkiField(name));
        return this;
    }

    /// <summary>Adds a configured field at the end of the field order.</summary>
    /// <param name="field">The field definition to validate and add.</param>
    /// <returns>This instance for fluent construction.</returns>
    /// <remarks>
    /// The record is retained as supplied, so its editor metadata remains available to serializers and package writers
    /// that support those attributes.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> or one of its required string values is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The field name or font is blank, or the name duplicates an existing field without regard to case.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="AnkiField.FontSize"/> is less than one.</exception>
    public AnkiNoteType AddConfiguredField(AnkiField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(field.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(field.Font);
        ArgumentOutOfRangeException.ThrowIfLessThan(field.FontSize, 1);
        if (fields.Any(existing => string.Equals(existing.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Field '{field.Name}' already exists.", nameof(field));
        }

        fields.Add(field);
        return this;
    }

    /// <summary>Adds a card template at the next ordinal.</summary>
    /// <param name="name">The non-empty template name, unique without regard to case.</param>
    /// <param name="questionFormat">Non-null Anki HTML and template markup for the front.</param>
    /// <param name="answerFormat">Non-null Anki HTML and template markup for the back.</param>
    /// <returns>This instance for fluent construction.</returns>
    /// <remarks>This overload leaves the optional browser-specific question and answer formats unset.</remarks>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank or duplicates a template name without regard to case.</exception>
    public AnkiNoteType AddTemplate(string name, string questionFormat, string answerFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(questionFormat);
        ArgumentNullException.ThrowIfNull(answerFormat);
        if (templates.Any(template => string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Template '{name}' already exists.", nameof(name));
        }

        templates.Add(new AnkiCardTemplate(name, questionFormat, answerFormat));
        return this;
    }

    /// <summary>Adds a configured card template at the next card ordinal.</summary>
    /// <param name="template">The template definition to validate and add.</param>
    /// <returns>This instance for fluent construction.</returns>
    /// <remarks>
    /// AnkiIO preserves all supplied markup, including optional browser-specific formats. Referenced field names are
    /// checked by <see cref="AnkiValidator.Validate(AnkiDeck)"/> after the note type is used by a note.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="template"/>, <see cref="AnkiCardTemplate.Name"/>, <see cref="AnkiCardTemplate.QuestionFormat"/>,
    /// or <see cref="AnkiCardTemplate.AnswerFormat"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The template name is blank or duplicates an existing template name without regard to case.
    /// </exception>
    public AnkiNoteType AddConfiguredTemplate(AnkiCardTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(template.Name);
        ArgumentNullException.ThrowIfNull(template.QuestionFormat);
        ArgumentNullException.ThrowIfNull(template.AnswerFormat);
        if (templates.Any(existing => string.Equals(existing.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Template '{template.Name}' already exists.", nameof(template));
        }

        templates.Add(template);
        return this;
    }

    internal bool HasEquivalentDefinition(AnkiNoteType other) =>
        string.Equals(Name, other.Name, StringComparison.Ordinal)
        && Kind == other.Kind
        && string.Equals(Css, other.Css, StringComparison.Ordinal)
        && fields.SequenceEqual(other.fields)
        && templates.SequenceEqual(other.templates);
}

/// <summary>Creates fresh conventional Anki note types with safe fields, templates, and CSS defaults.</summary>
/// <remarks>
/// Every method returns a new mutable note-type instance with a new identifier. Reuse the returned instance for related
/// notes, or use the convenience methods on <see cref="AnkiDeck"/>, which manage reuse within a deck hierarchy.
/// </remarks>
public static class AnkiNoteTypes
{
    private static readonly AnkiField[] BasicFields = [new("Front"), new("Back")];
    private static readonly AnkiField[] ClozeFields = [new("Text"), new("Extra")];
    private static readonly AnkiCardTemplate[] BasicTemplates =
    [
        new("Card 1", "{{Front}}", "{{FrontSide}}<hr id=\"answer\">{{Back}}"),
    ];
    private static readonly AnkiCardTemplate[] BasicAndReversedTemplates =
    [
        .. BasicTemplates,
        new("Card 2", "{{Back}}", "{{FrontSide}}<hr id=\"answer\">{{Front}}"),
    ];
    private static readonly AnkiCardTemplate[] ClozeTemplates =
    [
        new("Cloze", "{{cloze:Text}}", "{{cloze:Text}}<br>{{Extra}}"),
    ];

    /// <summary>Creates a Basic note type that generates one front-to-back card.</summary>
    /// <returns>A new standard note type with <c>Front</c> and <c>Back</c> fields and one card template.</returns>
    /// <remarks>The answer displays the rendered front, an answer separator, and the <c>Back</c> field.</remarks>
    public static AnkiNoteType CreateBasic() => Create(
        "Basic",
        AnkiNoteTypeKind.Standard,
        BasicFields,
        BasicTemplates);

    /// <summary>Creates a Basic (and reversed) type that generates two study directions.</summary>
    /// <returns>A new standard note type with <c>Front</c> and <c>Back</c> fields and two reciprocal templates.</returns>
    /// <remarks>Each note generates a front-to-back card followed by a back-to-front card.</remarks>
    public static AnkiNoteType CreateBasicAndReversed() => Create(
        "Basic (and reversed card)",
        AnkiNoteTypeKind.Standard,
        BasicFields,
        BasicAndReversedTemplates);

    /// <summary>Creates a Cloze type with Text and Extra fields.</summary>
    /// <returns>A new cloze note type with <c>Text</c> and <c>Extra</c> fields and one cloze template.</returns>
    /// <remarks>Cards are generated from distinct positive cloze indexes in the <c>Text</c> field, not from template count.</remarks>
    public static AnkiNoteType CreateCloze() => Create(
        "Cloze",
        AnkiNoteTypeKind.Cloze,
        ClozeFields,
        ClozeTemplates);

    internal static bool IsBasic(AnkiNoteType noteType) => Matches(
        noteType,
        "Basic",
        AnkiNoteTypeKind.Standard,
        BasicFields,
        BasicTemplates);

    internal static bool IsBasicAndReversed(AnkiNoteType noteType) => Matches(
        noteType,
        "Basic (and reversed card)",
        AnkiNoteTypeKind.Standard,
        BasicFields,
        BasicAndReversedTemplates);

    internal static bool IsCloze(AnkiNoteType noteType) => Matches(
        noteType,
        "Cloze",
        AnkiNoteTypeKind.Cloze,
        ClozeFields,
        ClozeTemplates);

    private static AnkiNoteType Create(
        string name,
        AnkiNoteTypeKind kind,
        IEnumerable<AnkiField> fields,
        IEnumerable<AnkiCardTemplate> templates)
    {
        var noteType = new AnkiNoteType(name, kind);
        foreach (var field in fields)
        {
            noteType.AddConfiguredField(field);
        }

        foreach (var template in templates)
        {
            noteType.AddConfiguredTemplate(template);
        }

        return noteType;
    }

    private static bool Matches(
        AnkiNoteType noteType,
        string name,
        AnkiNoteTypeKind kind,
        IReadOnlyList<AnkiField> fields,
        IReadOnlyList<AnkiCardTemplate> templates) =>
        string.Equals(noteType.Name, name, StringComparison.Ordinal)
        && noteType.Kind == kind
        && string.Equals(noteType.Css, AnkiNoteType.DefaultCss, StringComparison.Ordinal)
        && noteType.Fields.SequenceEqual(fields)
        && noteType.Templates.SequenceEqual(templates);
}
