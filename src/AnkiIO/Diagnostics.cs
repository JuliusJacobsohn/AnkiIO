using System.Text.RegularExpressions;

namespace AnkiIO;

/// <summary>Indicates how strongly a diagnostic should affect an operation.</summary>
public enum AnkiDiagnosticSeverity
{
    /// <summary>Informational compatibility or preservation detail.</summary>
    Information,
    /// <summary>Suspicious content that can still be represented.</summary>
    Warning,
    /// <summary>Invalid content that prevents a safe write.</summary>
    Error,
}

/// <summary>Provides machine-readable context for an inspectable content problem.</summary>
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
    string? SuggestedRemediation = null);

/// <summary>Contains all diagnostics produced by one validation pass.</summary>
public sealed class AnkiValidationResult
{
    internal AnkiValidationResult(IReadOnlyList<AnkiDiagnostic> diagnostics) => Diagnostics = diagnostics;

    /// <summary>Gets ordered structured diagnostics.</summary>
    public IReadOnlyList<AnkiDiagnostic> Diagnostics { get; }

    /// <summary>Gets whether no error-severity diagnostic was produced.</summary>
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != AnkiDiagnosticSeverity.Error);
}

/// <summary>Validates domain invariants before serialization or package creation.</summary>
public static partial class AnkiValidator
{
    /// <summary>Validates a complete deck hierarchy without mutating it.</summary>
    public static AnkiValidationResult Validate(AnkiDeck root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var diagnostics = new List<AnkiDiagnostic>();
        var ids = new HashSet<long>();

        foreach (var deck in root.Traverse())
        {
            AddDuplicate(ids, deck.Id, "ANKI001", "Duplicate deck ID.", diagnostics, deck.Id);
            foreach (var note in deck.Notes)
            {
                AddDuplicate(ids, note.Id, "ANKI002", "Duplicate note ID.", diagnostics, deck.Id, note.Id);
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
