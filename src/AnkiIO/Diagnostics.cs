using System.Text.RegularExpressions;

namespace AnkiIO;

/// <summary>Indicates how a diagnostic affects inspection, serialization, or package creation.</summary>
/// <remarks>Consumers should branch on severity or stable diagnostic code instead of parsing human-readable messages.</remarks>
public enum AnkiDiagnosticSeverity
{
    /// <summary>Reports a compatibility or preservation detail that requires no corrective action.</summary>
    Information,
    /// <summary>Reports suspicious or lossy content that can still be represented.</summary>
    Warning,
    /// <summary>Reports invalid content that prevents a safe validated write.</summary>
    Error,
}

/// <summary>Describes one machine-readable validation, compatibility, or preservation finding.</summary>
/// <param name="Severity">The operational impact of the finding.</param>
/// <param name="Code">A stable non-localized code intended for filtering and automation, such as <c>ANKI020</c>.</param>
/// <param name="Message">A human-readable explanation intended for logs or user interfaces.</param>
/// <param name="Location">An optional source path, archive entry, JSON location, or adapter-specific logical location.</param>
/// <param name="DeckId">The optional stable identifier of the related deck.</param>
/// <param name="NoteId">The optional stable identifier of the related note.</param>
/// <param name="CardId">The optional stable identifier of the related card.</param>
/// <param name="FieldName">The optional related field name using the spelling stored by its note type.</param>
/// <param name="MediaFileName">The optional related package media filename.</param>
/// <param name="SuggestedRemediation">Optional concise guidance for correcting or safely handling the finding.</param>
/// <remarks>
/// The record is immutable. Context properties are independently optional because some findings apply to a complete
/// input rather than one domain object. Codes are the compatibility contract; messages and remediation text may evolve.
/// </remarks>
/// <example>
/// <code>
/// foreach (var diagnostic in AnkiValidator.Validate(deck).Diagnostics)
/// {
///     Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
/// }
/// </code>
/// </example>
public sealed record AnkiDiagnostic(
    AnkiDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Location = null,
    long? DeckId = null,
    long? NoteId = null,
    long? CardId = null,
    string? FieldName = null,
    string? MediaFileName = null,
    string? SuggestedRemediation = null)
{
    /// <summary>Gets the operational impact of this finding.</summary>
    /// <value>The severity supplied to the primary constructor.</value>
    public AnkiDiagnosticSeverity Severity { get; init; } = Severity;

    /// <summary>Gets the stable non-localized code intended for filtering and automation.</summary>
    /// <value>The diagnostic code supplied to the primary constructor.</value>
    public string Code { get; init; } = Code;

    /// <summary>Gets the human-readable explanation of this finding.</summary>
    /// <value>The message supplied to the primary constructor.</value>
    public string Message { get; init; } = Message;

    /// <summary>Gets the optional source or logical location associated with this finding.</summary>
    /// <value>A path, archive entry, JSON location, or adapter-defined location; otherwise, <see langword="null"/>.</value>
    public string? Location { get; init; } = Location;

    /// <summary>Gets the optional stable identifier of the related deck.</summary>
    /// <value>The related deck identifier, or <see langword="null"/> when the finding is not deck-specific.</value>
    public long? DeckId { get; init; } = DeckId;

    /// <summary>Gets the optional stable identifier of the related note.</summary>
    /// <value>The related note identifier, or <see langword="null"/> when the finding is not note-specific.</value>
    public long? NoteId { get; init; } = NoteId;

    /// <summary>Gets the optional stable identifier of the related card.</summary>
    /// <value>The related card identifier, or <see langword="null"/> when the finding is not card-specific.</value>
    public long? CardId { get; init; } = CardId;

    /// <summary>Gets the optional note-field name associated with this finding.</summary>
    /// <value>The stored field name, or <see langword="null"/> when the finding is not field-specific.</value>
    public string? FieldName { get; init; } = FieldName;

    /// <summary>Gets the optional package media filename associated with this finding.</summary>
    /// <value>The media filename, or <see langword="null"/> when the finding is not media-specific.</value>
    public string? MediaFileName { get; init; } = MediaFileName;

    /// <summary>Gets optional guidance for correcting or safely handling this finding.</summary>
    /// <value>A concise remediation suggestion, or <see langword="null"/> when none is available.</value>
    public string? SuggestedRemediation { get; init; } = SuggestedRemediation;
}

/// <summary>Contains the ordered findings and validity decision produced by one validation pass.</summary>
/// <remarks>
/// Validation is non-throwing for content problems: inspect <see cref="Diagnostics"/> for detail and <see cref="IsValid"/>
/// for the write/no-write decision. The result is a stable snapshot and can be read concurrently after construction.
/// </remarks>
public sealed class AnkiValidationResult
{
    internal AnkiValidationResult(IReadOnlyList<AnkiDiagnostic> diagnostics) => Diagnostics = diagnostics;

    /// <summary>Gets ordered structured diagnostics.</summary>
    /// <value>All findings in deterministic traversal order; the collection is empty when no issue was found.</value>
    public IReadOnlyList<AnkiDiagnostic> Diagnostics { get; }

    /// <summary>Gets whether no error-severity diagnostic was produced.</summary>
    /// <value><see langword="true"/> when <see cref="Diagnostics"/> contains no <see cref="AnkiDiagnosticSeverity.Error"/> entry.</value>
    /// <remarks>Warnings and informational diagnostics do not make a result invalid.</remarks>
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != AnkiDiagnosticSeverity.Error);
}

/// <summary>Validates deck-domain invariants before serialization or package creation.</summary>
/// <remarks>
/// Validation inspects a complete hierarchy without mutating it. Current checks include identifier uniqueness, conflicting
/// note-type definitions, required note-type structure, template field references, card type/queue consistency, new-card
/// counters, and card flag range.
/// File readers may report additional format-specific diagnostics separately.
/// </remarks>
public static partial class AnkiValidator
{
    /// <summary>Validates a complete deck hierarchy without mutating it.</summary>
    /// <param name="root">The top-level deck whose direct notes and descendants should be inspected.</param>
    /// <returns>A snapshot containing every discovered diagnostic and the aggregate validity decision.</returns>
    /// <remarks>
    /// Content errors are returned as diagnostics rather than thrown. The method enumerates mutable deck state, so callers
    /// must not modify the hierarchy concurrently.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// var result = AnkiValidator.Validate(deck);
    /// if (!result.IsValid)
    /// {
    ///     throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
    /// }
    /// </code>
    /// </example>
    public static AnkiValidationResult Validate(AnkiDeck root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var diagnostics = new List<AnkiDiagnostic>();
        var ids = new HashSet<long>();
        var noteTypesById = new Dictionary<long, AnkiNoteType>();
        var reportedNoteTypeConflicts = new HashSet<long>();

        foreach (var deck in root.Traverse())
        {
            AddDuplicate(ids, deck.Id, "ANKI001", "Duplicate deck ID.", diagnostics, deck.Id);
            foreach (var note in deck.Notes)
            {
                AddDuplicate(ids, note.Id, "ANKI002", "Duplicate note ID.", diagnostics, deck.Id, note.Id);
                if (!noteTypesById.TryAdd(note.NoteType.Id, note.NoteType)
                    && !noteTypesById[note.NoteType.Id].HasEquivalentDefinition(note.NoteType)
                    && reportedNoteTypeConflicts.Add(note.NoteType.Id))
                {
                    diagnostics.Add(new(
                        AnkiDiagnosticSeverity.Error,
                        "ANKI004",
                        $"Note type ID {note.NoteType.Id} is shared by conflicting definitions.",
                        DeckId: deck.Id,
                        NoteId: note.Id,
                        SuggestedRemediation: "Assign a unique ID to each distinct note-type definition."));
                }

                if (note.NoteType.Fields.Count == 0)
                {
                    diagnostics.Add(new(AnkiDiagnosticSeverity.Error, "ANKI010", "The note type has no fields.", DeckId: deck.Id, NoteId: note.Id));
                }

                if (note.NoteType.Templates.Count == 0)
                {
                    diagnostics.Add(new(AnkiDiagnosticSeverity.Error, "ANKI011", "The note type has no templates.", DeckId: deck.Id, NoteId: note.Id));
                }

                ValidateTemplates(note, deck.Id, diagnostics);
                foreach (var card in note.Cards)
                {
                    AddDuplicate(ids, card.Id, "ANKI003", "Duplicate card ID.", diagnostics, deck.Id, note.Id, card.Id);
                    ValidateScheduling(card, diagnostics);
                }
            }
        }

        return new AnkiValidationResult(diagnostics);
    }

    private static void ValidateTemplates(AnkiNote note, long deckId, List<AnkiDiagnostic> diagnostics)
    {
        var known = note.NoteType.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        known.Add("FrontSide");
        foreach (var template in note.NoteType.Templates)
        {
            foreach (Match match in TemplateFieldPattern().Matches(template.QuestionFormat + template.AnswerFormat))
            {
                var name = match.Groups[1].Value.Split(':', StringSplitOptions.RemoveEmptyEntries)[^1];
                if (!known.Contains(name))
                {
                    diagnostics.Add(new(AnkiDiagnosticSeverity.Error, "ANKI020", $"Template '{template.Name}' references unknown field '{name}'.", DeckId: deckId, NoteId: note.Id, FieldName: name, SuggestedRemediation: "Add the field or correct the template reference."));
                }
            }
        }
    }

    private static void ValidateScheduling(AnkiCard card, List<AnkiDiagnostic> diagnostics)
    {
        var value = card.Scheduling;
        var activeQueueValid = value.Type switch
        {
            AnkiCardType.New => value.Queue == AnkiCardQueue.New,
            AnkiCardType.Learning => value.Queue is AnkiCardQueue.Learning or AnkiCardQueue.DayLearning,
            AnkiCardType.Review => value.Queue == AnkiCardQueue.Review,
            AnkiCardType.Relearning => value.Queue is AnkiCardQueue.Learning or AnkiCardQueue.DayLearning,
            _ => false,
        };
        var inactive = value.Queue is AnkiCardQueue.Suspended or AnkiCardQueue.SiblingBuried or AnkiCardQueue.SchedulerBuried;
        if (!activeQueueValid && !inactive)
        {
            diagnostics.Add(new(AnkiDiagnosticSeverity.Error, "ANKI030", $"Card type {value.Type} is inconsistent with queue {value.Queue}.", CardId: card.Id, SuggestedRemediation: "Use a queue appropriate for the phase, or a suspended/buried queue."));
        }

        if (value.Type == AnkiCardType.New && (value.Interval != 0 || value.Repetitions != 0 || value.Lapses != 0))
        {
            diagnostics.Add(new(AnkiDiagnosticSeverity.Error, "ANKI031", "A new card cannot have review interval, repetitions, or lapses.", CardId: card.Id, SuggestedRemediation: "Clear review-derived values or select an explicit learning/review type."));
        }

        if (card.Flag is < 0 or > 7)
        {
            diagnostics.Add(new(AnkiDiagnosticSeverity.Error, "ANKI032", "Card flag must be between 0 and 7.", CardId: card.Id));
        }
    }

    private static void AddDuplicate(HashSet<long> ids, long id, string code, string message, List<AnkiDiagnostic> diagnostics, long? deckId = null, long? noteId = null, long? cardId = null)
    {
        if (!ids.Add(id))
        {
            diagnostics.Add(new(AnkiDiagnosticSeverity.Error, code, message, DeckId: deckId, NoteId: noteId, CardId: cardId, SuggestedRemediation: "Assign a unique stable identifier."));
        }
    }

    [GeneratedRegex(@"\{\{[#/^]?([^{}]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateFieldPattern();
}
