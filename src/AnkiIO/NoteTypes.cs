namespace AnkiIO;

/// <summary>Identifies how a note type generates cards.</summary>
public enum AnkiNoteTypeKind
{
    /// <summary>Each applicable template produces one card.</summary>
    Standard,

    /// <summary>Cloze indexes in the text produce cards.</summary>
    Cloze,
}

/// <summary>Defines an ordered note field.</summary>
/// <param name="Name">The unique field name within its note type.</param>
/// <param name="IsRightToLeft">Whether editors should use right-to-left direction.</param>
/// <param name="IsSticky">Whether Anki editors may retain this value for the next note.</param>
/// <param name="Font">The editor font preference.</param>
/// <param name="FontSize">The editor font size in pixels.</param>
public sealed record AnkiField(
    string Name,
    bool IsRightToLeft = false,
    bool IsSticky = false,
    string Font = "Arial",
    int FontSize = 20);

/// <summary>Defines how note fields form the question and answer of a generated card.</summary>
/// <param name="Name">The template's display name.</param>
/// <param name="QuestionFormat">Anki HTML/template markup for the card front.</param>
/// <param name="AnswerFormat">Anki HTML/template markup for the card back.</param>
/// <param name="BrowserQuestionFormat">Optional browser-only front markup.</param>
/// <param name="BrowserAnswerFormat">Optional browser-only back markup.</param>
public sealed record AnkiCardTemplate(
    string Name,
    string QuestionFormat,
    string AnswerFormat,
    string? BrowserQuestionFormat = null,
    string? BrowserAnswerFormat = null);

/// <summary>Defines fields, templates, and shared CSS for a family of notes.</summary>
/// <remarks>Instances use controlled mutation while being assembled. Notes retain a reference to the type, so callers should finish configuring it before adding notes.</remarks>
public sealed class AnkiNoteType
{
    private readonly List<AnkiField> fields = [];
    private readonly List<AnkiCardTemplate> templates = [];

    /// <summary>Initializes an empty standard note type.</summary>
    /// <param name="name">The user-visible note type name.</param>
    /// <param name="kind">The card-generation strategy.</param>
    /// <param name="id">A stable ID, or <see langword="null"/> to generate one.</param>
    public AnkiNoteType(string name, AnkiNoteTypeKind kind = AnkiNoteTypeKind.Standard, long? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Kind = kind;
        Id = id ?? AnkiId.New();
    }

    /// <summary>Gets the stable numeric note-type identifier.</summary>
    public long Id { get; }

    /// <summary>Gets the user-visible name.</summary>
    public string Name { get; }

    /// <summary>Gets the card-generation strategy.</summary>
    public AnkiNoteTypeKind Kind { get; }

    /// <summary>Gets or sets shared card CSS.</summary>
    public string Css { get; set; } = ".card { font-family: Arial; font-size: 20px; text-align: center; color: black; background: white; }";

    /// <summary>Gets fields in their storage and display order.</summary>
    public IReadOnlyList<AnkiField> Fields => fields;

    /// <summary>Gets templates in card-ordinal order.</summary>
    public IReadOnlyList<AnkiCardTemplate> Templates => templates;

    /// <summary>Adds a uniquely named field at the end of the field order.</summary>
    /// <param name="name">The field name used by notes and template references.</param>
    /// <returns>This instance for fluent construction.</returns>
    /// <exception cref="ArgumentException">The name is blank or duplicates a field ignoring case.</exception>
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

    /// <summary>Adds a card template at the next ordinal.</summary>
    /// <param name="name">The unique template name.</param>
    /// <param name="questionFormat">Anki template markup for the front.</param>
    /// <param name="answerFormat">Anki template markup for the back.</param>
    /// <returns>This instance for fluent construction.</returns>
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
}

/// <summary>Provides conventional note types with safe defaults.</summary>
public static class AnkiNoteTypes
{
    /// <summary>Creates a Basic note type that generates one front-to-back card.</summary>
    public static AnkiNoteType CreateBasic() => new AnkiNoteType("Basic")
        .AddField("Front")
        .AddField("Back")
        .AddTemplate("Card 1", "{{Front}}", "{{FrontSide}}<hr id=\"answer\">{{Back}}");

    /// <summary>Creates a Basic (and reversed) type that generates two study directions.</summary>
    public static AnkiNoteType CreateBasicAndReversed() => new AnkiNoteType("Basic (and reversed card)")
        .AddField("Front")
        .AddField("Back")
        .AddTemplate("Card 1", "{{Front}}", "{{FrontSide}}<hr id=\"answer\">{{Back}}")
        .AddTemplate("Card 2", "{{Back}}", "{{FrontSide}}<hr id=\"answer\">{{Front}}");

    /// <summary>Creates a Cloze type with Text and Extra fields.</summary>
    public static AnkiNoteType CreateCloze() => new AnkiNoteType("Cloze", AnkiNoteTypeKind.Cloze)
        .AddField("Text")
        .AddField("Extra")
        .AddTemplate("Cloze", "{{cloze:Text}}", "{{cloze:Text}}<br>{{Extra}}");
}

