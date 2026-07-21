using System.Text.RegularExpressions;

namespace AnkiIO;

/// <summary>Checks a complete deck hierarchy before native JSON, CrowdAnki-style JSON, or APKG output.</summary>
/// <remarks>
/// Validation is read-only and returns every detected content error instead of failing at the first one. Checks cover
/// duplicate object IDs, conflicting note-type definitions, missing fields or templates, template field references,
/// scheduler type/queue combinations, non-negative review counters, new-card counters, and the three-bit card flag.
/// <para>
/// Active scheduling pairs are New/New, Learning/Learning, Learning/DayLearning, Review/Review,
/// Relearning/Learning, and Relearning/DayLearning. Suspended, sibling-buried, scheduler-buried, and preview queues retain
/// any defined underlying card type. Validation preserves queue-specific values; it does not run Anki's scheduler or infer
/// a new due date.
/// </para>
/// The hierarchy is mutable and not thread-safe, so callers must not change it while validation enumerates it.
/// </remarks>
public static partial class AnkiValidator
{
    /// <summary>Validates a root deck and every reachable descendant without modifying them.</summary>
    /// <param name="root">The root of the hierarchy to inspect.</param>
    /// <returns>An immutable diagnostic snapshot and aggregate validity decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// AnkiValidationResult result = AnkiValidator.Validate(deck);
    /// if (!result.IsValid)
    /// {
    ///     foreach (AnkiDiagnostic error in result.Diagnostics.Where(d => d.Severity == AnkiDiagnosticSeverity.Error))
    ///     {
    ///         Console.Error.WriteLine($"{error.Code}: {error.Message}");
    ///     }
    /// }
    /// </code>
    /// </example>
    public static AnkiValidationResult Validate(AnkiDeck root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return ValidateCore([root]);
    }

    /// <summary>Validates several top-level deck hierarchies as one output graph.</summary>
    /// <param name="roots">
    /// The non-empty sequence of hierarchy roots to inspect. The sequence is enumerated once into a snapshot before
    /// validation starts; individual deck graphs remain live and must not be mutated concurrently.
    /// </param>
    /// <returns>An immutable diagnostic snapshot whose uniqueness checks are shared across every supplied hierarchy.</returns>
    /// <remarks>
    /// Use this overload before producing a package that contains more than one root. Calling
    /// <see cref="Validate(AnkiDeck)"/> separately for each root cannot detect an ID reused by two different hierarchies or
    /// two conflicting note-type definitions with the same ID. Package writers use this overload automatically.
    ///
    /// <para>
    /// This validates deck-domain structure and scheduler state. It does not inspect ZIP safety, media payload availability,
    /// or same-name media collisions; those checks belong to the package reader/writer because they require archive or
    /// stream access.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// AnkiValidationResult result = AnkiValidator.Validate(new[] { germanDeck, spanishDeck });
    /// if (!result.IsValid)
    /// {
    ///     throw new AnkiValidationException(result);
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="roots"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="roots"/> is empty or contains a <see langword="null"/> hierarchy root.
    /// </exception>
    public static AnkiValidationResult Validate(IEnumerable<AnkiDeck> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var snapshot = roots.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("At least one deck hierarchy is required.", nameof(roots));
        }

        if (snapshot.Any(root => root is null))
        {
            throw new ArgumentException("A deck hierarchy cannot be null.", nameof(roots));
        }

        return ValidateCore(snapshot);
    }

    private static AnkiValidationResult ValidateCore(IReadOnlyList<AnkiDeck> roots)
    {
        var diagnostics = new List<AnkiDiagnostic>();
        var ids = new HashSet<long>();
        var noteTypesById = new Dictionary<long, AnkiNoteType>();
        var reportedNoteTypeConflicts = new HashSet<long>();

        foreach (var deck in roots.SelectMany(root => root.Traverse()))
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
        var knownType = value.Type is AnkiCardType.New or AnkiCardType.Learning or AnkiCardType.Review or AnkiCardType.Relearning;
        var activeQueueValid = value.Type switch
        {
            AnkiCardType.New => value.Queue == AnkiCardQueue.New,
            AnkiCardType.Learning => value.Queue is AnkiCardQueue.Learning or AnkiCardQueue.DayLearning,
            AnkiCardType.Review => value.Queue == AnkiCardQueue.Review,
            AnkiCardType.Relearning => value.Queue is AnkiCardQueue.Learning or AnkiCardQueue.DayLearning,
            _ => false,
        };
        var specialQueue = value.Queue is AnkiCardQueue.Suspended
            or AnkiCardQueue.SiblingBuried
            or AnkiCardQueue.SchedulerBuried
            or AnkiCardQueue.Preview;
        if (!knownType || (!activeQueueValid && !specialQueue))
        {
            diagnostics.Add(new(AnkiDiagnosticSeverity.Error, "ANKI030", $"Card type {value.Type} is inconsistent with queue {value.Queue}.", CardId: card.Id, SuggestedRemediation: "Use a queue appropriate for the phase, or a supported suspended, buried, or preview queue."));
        }

        if (value.Type == AnkiCardType.New && (value.Interval != 0 || value.Repetitions != 0 || value.Lapses != 0))
        {
            diagnostics.Add(new(AnkiDiagnosticSeverity.Error, "ANKI031", "A new card cannot have review interval, repetitions, or lapses.", CardId: card.Id, SuggestedRemediation: "Clear review-derived values or select an explicit learning/review type."));
        }

        if (card.Flag is < 0 or > 7)
        {
            diagnostics.Add(new(AnkiDiagnosticSeverity.Error, "ANKI032", "Card flag must be between 0 and 7.", CardId: card.Id));
        }

        if (value.Repetitions < 0 || value.Lapses < 0)
        {
            diagnostics.Add(new(AnkiDiagnosticSeverity.Error, "ANKI033", "Scheduling repetition and lapse counters cannot be negative.", CardId: card.Id, SuggestedRemediation: "Use zero for an uninitialized counter or a non-negative persisted count."));
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
