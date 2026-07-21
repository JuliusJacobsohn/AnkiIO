using System.Collections.ObjectModel;

namespace AnkiIO;

/// <summary>Defines the reusable schema and rendering rules shared by a family of Anki notes.</summary>
/// <remarks>
/// An Anki note type (called a “model” by some file formats) is comparable to a small schema: its ordered fields define
/// what each note stores, its templates define the cards Standard notes generate, and its CSS styles every generated card.
/// Reuse one instance for all notes that share that schema. Creating a new instance for every note gives each instance a
/// different ID and can produce unnecessary duplicate note types after import.
///
/// <para>
/// Build the definition first, then create notes. Constructing the first <see cref="AnkiNote"/> freezes the instance
/// permanently so later changes cannot shift positional field storage or card ordinals beneath existing notes. The
/// <see cref="Fields"/> and <see cref="Templates"/> collections are ordered, live read-only views; their elements are
/// immutable records. AnkiIO object graphs are not safe for concurrent mutation.
/// </para>
///
/// <para>
/// Field and template names are unique without regard to case, but note-value lookup uses the stored spelling exactly.
/// AnkiIO validates structure and known field references before output; it does not render templates, sanitize HTML/CSS,
/// run JavaScript, or promise that custom markup works in every Anki version.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var vocabulary = new AnkiNoteType("Vocabulary")
///     .AddConfiguredField(new AnkiField("German", FontSize: 24))
///     .AddField("Meaning")
///     .AddConfiguredTemplate(new AnkiCardTemplate(
///         "Recognition",
///         "{{German}}",
///         "{{FrontSide}}&lt;hr id=\"answer\"&gt;{{Meaning}}"));
/// vocabulary.Css += ".hint { color: #777; }";
///
/// var deck = new AnkiDeck("German");
/// deck.AddNote(vocabulary, new Dictionary&lt;string, string&gt;
/// {
///     ["German"] = "die Straße",
///     ["Meaning"] = "street",
/// });
/// // vocabulary.IsFrozen is now true; reuse it for more Vocabulary notes.
/// </code>
/// </example>
public sealed class AnkiNoteType
{
    internal const string DefaultCss = ".card { font-family: Arial; font-size: 20px; text-align: center; color: black; background: white; }";

    private readonly List<AnkiField> fields = [];
    private readonly List<AnkiCardTemplate> templates = [];
    private readonly ReadOnlyCollection<AnkiField> fieldsView;
    private readonly ReadOnlyCollection<AnkiCardTemplate> templatesView;
    private string css = DefaultCss;
    private bool isFrozen;

    /// <summary>Initializes an unfrozen note type with no fields or templates and default card CSS.</summary>
    /// <param name="name">The non-blank name shown in Anki's note-type manager.</param>
    /// <param name="kind">
    /// <see cref="AnkiNoteTypeKind.Standard"/> for template-per-card generation, or
    /// <see cref="AnkiNoteTypeKind.Cloze"/> for deletion-index generation.
    /// </param>
    /// <param name="id">
    /// An optional stable numeric identifier for repeatable imports; when omitted, <see cref="AnkiId.New"/> generates one.
    /// </param>
    /// <remarks>
    /// Empty definitions are allowed while assembling a model, but validation rejects a used note type with no fields or
    /// templates. For a ready-made conventional definition, use <see cref="AnkiNoteTypes"/>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or consists only of whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public AnkiNoteType(string name, AnkiNoteTypeKind kind = AnkiNoteTypeKind.Standard, long? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Kind = kind;
        Id = id ?? AnkiId.New();
        fieldsView = fields.AsReadOnly();
        templatesView = templates.AsReadOnly();
    }

    /// <summary>Gets the persisted identity by which notes and package metadata refer to this definition.</summary>
    /// <value>The caller-supplied identifier, or a process-generated positive identifier.</value>
    /// <remarks>
    /// Preserve an imported ID when updating the same definition. Distinct definitions must not share an ID in one output,
    /// even if their names match; validation reports conflicting definitions. Explicit ID collision avoidance is the
    /// caller's responsibility.
    /// </remarks>
    public long Id { get; }

    /// <summary>Gets the user-visible note-type name.</summary>
    /// <value>The name supplied when this instance was constructed.</value>
    public string Name { get; }

    /// <summary>Gets the immutable strategy used to turn one note into cards.</summary>
    /// <value>The immutable <see cref="AnkiNoteTypeKind"/> selected at construction.</value>
    public AnkiNoteTypeKind Kind { get; }

    /// <summary>Gets or sets CSS shared by every card rendered from this note type.</summary>
    /// <value>Anki-compatible CSS. The default renders centered black Arial text on a white background.</value>
    /// <remarks>
    /// The value is stored verbatim and emitted alongside the templates. It controls study-card rendering; editor-only
    /// font settings live on <see cref="AnkiField"/>. AnkiIO does not parse, sanitize, prefix, or normalize CSS, so callers
    /// accepting untrusted styles must apply their own content policy. Assignment is rejected after first use.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The note type is frozen because a note already uses it.</exception>
    public string Css
    {
        get => css;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            EnsureMutable();
            css = value;
        }
    }

    /// <summary>Gets whether fields, templates, and CSS can no longer be changed.</summary>
    /// <value>
    /// <see langword="true"/> after this instance has been supplied to an <see cref="AnkiNote"/> constructor; otherwise,
    /// <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// Freezing is automatic and permanent. It protects every note that retains this shared definition from later changes
    /// to its field order, card ordinals, or rendering CSS.
    /// </remarks>
    public bool IsFrozen => isFrozen;

    /// <summary>Gets fields in positional storage and Anki-editor order.</summary>
    /// <value>A live read-only view that reflects fields added before the note type freezes.</value>
    /// <remarks>
    /// Use this order when importing positional values. Field-value dictionaries are keyed by exact name, but legacy Anki
    /// databases serialize their values according to this list.
    /// </remarks>
    public IReadOnlyList<AnkiField> Fields => fieldsView;

    /// <summary>Gets templates in zero-based card-ordinal order.</summary>
    /// <value>A live read-only view that reflects templates added before the note type freezes.</value>
    /// <remarks>
    /// Standard notes generate one card for each entry. Cloze notes use distinct positive deletion indexes instead; their
    /// conventional definition still needs a template describing how <c>{{cloze:Text}}</c> renders.
    /// </remarks>
    public IReadOnlyList<AnkiCardTemplate> Templates => templatesView;

    /// <summary>Adds a uniquely named field at the end of the field order.</summary>
    /// <param name="name">The non-empty field name used by notes and template references.</param>
    /// <returns>This instance for fluent construction.</returns>
    /// <remarks>
    /// This overload creates a field with left-to-right, non-sticky, Arial 20-pixel editor defaults. Those settings affect
    /// the editor, not study-card CSS. Use <see cref="AddConfiguredField"/> for other metadata.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank or duplicates a field name without regard to case.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The note type is frozen because a note already uses it.</exception>
    public AnkiNoteType AddField(string name)
    {
        EnsureMutable();
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
    /// The immutable record is retained as supplied, so its editor metadata remains available to serializers and package
    /// writers that support those attributes. Adding it after any note has been constructed is forbidden because that
    /// would change every note's positional field schema.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> or one of its required string values is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The field name or font is blank, or the name duplicates an existing field without regard to case.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="AnkiField.FontSize"/> is less than one.</exception>
    /// <exception cref="InvalidOperationException">The note type is frozen because a note already uses it.</exception>
    public AnkiNoteType AddConfiguredField(AnkiField field)
    {
        EnsureMutable();
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
    /// <remarks>
    /// This overload leaves browser-specific formats unset. Markup is stored verbatim and field references are validated
    /// later; AnkiIO does not render the template or suppress cards whose conditional front would be empty.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank or duplicates a template name without regard to case.</exception>
    /// <exception cref="InvalidOperationException">The note type is frozen because a note already uses it.</exception>
    public AnkiNoteType AddTemplate(string name, string questionFormat, string answerFormat)
    {
        EnsureMutable();
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
    /// AnkiIO preserves all supplied markup, including optional browser-specific formats. Known-field checking is performed
    /// on the main front/back formats by <see cref="AnkiValidator.Validate(AnkiDeck)"/> after the note type is used, but that
    /// validation is not a full template renderer or HTML security check. See <see cref="AnkiCardTemplate"/> for supported
    /// conventions and limitations.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="template"/>, <see cref="AnkiCardTemplate.Name"/>, <see cref="AnkiCardTemplate.QuestionFormat"/>,
    /// or <see cref="AnkiCardTemplate.AnswerFormat"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The template name is blank or duplicates an existing template name without regard to case.
    /// </exception>
    /// <exception cref="InvalidOperationException">The note type is frozen because a note already uses it.</exception>
    public AnkiNoteType AddConfiguredTemplate(AnkiCardTemplate template)
    {
        EnsureMutable();
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

    internal void Freeze() => isFrozen = true;

    private void EnsureMutable()
    {
        if (isFrozen)
        {
            throw new InvalidOperationException($"Note type '{Name}' is frozen because it is already used by a note.");
        }
    }
}
