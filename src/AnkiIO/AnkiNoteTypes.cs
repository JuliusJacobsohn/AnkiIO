namespace AnkiIO;

/// <summary>Creates fresh conventional Basic, reversed, and Cloze definitions for callers that need direct control.</summary>
/// <remarks>
/// These factories mirror familiar Anki note types while avoiding a dependency on definitions installed in a user's
/// profile. Every call returns a different, initially mutable <see cref="AnkiNoteType"/> with a new ID. Configure its CSS
/// or add fields before creating the first note, then reuse that same instance for every note with the same schema.
///
/// <para>
/// For ordinary deck creation, prefer <see cref="AnkiDeck.AddBasicNote"/>,
/// <see cref="AnkiDeck.AddBasicAndReversedNote"/>, or <see cref="AnkiDeck.AddClozeNote"/>. Those helpers reuse one matching
/// definition across the complete deck hierarchy. Repeatedly calling these factories yourself without reusing the result
/// can create several note types with the same visible name but different IDs.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var type = AnkiNoteTypes.CreateBasicAndReversed();
/// type.Css += ".card { max-width: 42rem; margin: auto; }";
///
/// var deck = new AnkiDeck("German");
/// deck.AddNote(type, new Dictionary&lt;string, string&gt;
/// {
///     ["Front"] = "der Baum",
///     ["Back"] = "tree",
/// });
/// // Reuse 'type' for subsequent notes; it is frozen after the first AddNote call.
/// </code>
/// </example>
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

    /// <summary>Creates a conventional Basic definition that generates one front-to-back card.</summary>
    /// <returns>A new standard note type with <c>Front</c> and <c>Back</c> fields and one card template.</returns>
    /// <remarks>
    /// The front renders <c>{{Front}}</c>. The answer reuses <c>{{FrontSide}}</c>, adds Anki's conventional answer rule,
    /// then renders <c>{{Back}}</c>. Field values may contain HTML and media references and are not sanitized.
    /// </remarks>
    public static AnkiNoteType CreateBasic() => Create(
        "Basic",
        AnkiNoteTypeKind.Standard,
        BasicFields,
        BasicTemplates);

    /// <summary>Creates a conventional Basic (and reversed) definition that generates two study directions.</summary>
    /// <returns>A new standard note type with <c>Front</c> and <c>Back</c> fields and two reciprocal templates.</returns>
    /// <remarks>
    /// Every note always generates both directions, front-to-back first and back-to-front second. This is different from
    /// Anki's optional-reverse pattern, which conditionally creates a reverse card based on an extra field; create a custom
    /// Standard note type when conditional reversal is required.
    /// </remarks>
    public static AnkiNoteType CreateBasicAndReversed() => Create(
        "Basic (and reversed card)",
        AnkiNoteTypeKind.Standard,
        BasicFields,
        BasicAndReversedTemplates);

    /// <summary>Creates a conventional Cloze definition with <c>Text</c> and <c>Extra</c> fields.</summary>
    /// <returns>A new cloze note type with <c>Text</c> and <c>Extra</c> fields and one cloze template.</returns>
    /// <remarks>
    /// The front and back use <c>{{cloze:Text}}</c>; the answer additionally renders <c>Extra</c>. Cards are generated from
    /// distinct positive cloze indexes in <c>Text</c>, not from template count. Use <see cref="AnkiCloze.Wrap"/> to build
    /// simple deletions or supply valid advanced Anki markup yourself through the low-level note API.
    /// </remarks>
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
