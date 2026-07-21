namespace AnkiIO;

/// <summary>Provides one machine-readable validation, compatibility, or preservation finding.</summary>
/// <param name="Severity">Whether the finding is explanatory, lossy, or blocks a validated write.</param>
/// <param name="Code">A stable, non-localized identifier such as <c>ANKI020</c> or <c>CROWD002</c>.</param>
/// <param name="Message">A human-readable explanation for logs or user interfaces.</param>
/// <param name="Location">An optional JSON path, archive entry, source path, or adapter-defined logical location.</param>
/// <param name="DeckId">The related deck ID when the finding is deck-specific.</param>
/// <param name="NoteId">The related note ID when the finding is note-specific.</param>
/// <param name="CardId">The related card ID when the finding is card-specific.</param>
/// <param name="FieldName">The related note-type field using its stored case-sensitive spelling.</param>
/// <param name="MediaFileName">The related collection media filename.</param>
/// <param name="SuggestedRemediation">Optional concise guidance for correcting or safely handling the finding.</param>
/// <remarks>
/// The immutable record is safe to retain after validation. Branch on <see cref="Code"/> and <see cref="Severity"/> rather
/// than parsing <see cref="Message"/>, because wording and remediation may improve without a breaking API change. Context
/// properties are independently optional: an archive-wide security or compatibility finding may have no object ID, while
/// a template failure normally carries deck, note, and field context.
/// </remarks>
/// <example>
/// <code>
/// foreach (AnkiDiagnostic diagnostic in AnkiValidator.Validate(deck).Diagnostics)
/// {
///     if (diagnostic.Severity == AnkiDiagnosticSeverity.Error)
///     {
///         Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
///     }
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
    /// <summary>Gets the operational impact of the finding.</summary>
    /// <value>Information, warning, or error; only error invalidates a validation result.</value>
    public AnkiDiagnosticSeverity Severity { get; init; } = Severity;

    /// <summary>Gets the stable identifier intended for filtering and automation.</summary>
    /// <value>A non-localized code whose meaning is part of the compatibility contract.</value>
    public string Code { get; init; } = Code;

    /// <summary>Gets the current human-readable explanation.</summary>
    /// <value>Text suitable for display or logs, but not for machine parsing.</value>
    public string Message { get; init; } = Message;

    /// <summary>Gets optional source-format context.</summary>
    /// <value>A path, entry name, JSON location, or logical adapter location; otherwise <see langword="null"/>.</value>
    public string? Location { get; init; } = Location;

    /// <summary>Gets the related deck identifier when available.</summary>
    /// <value>A stable deck ID, or <see langword="null"/> for findings not tied to one deck.</value>
    public long? DeckId { get; init; } = DeckId;

    /// <summary>Gets the related note identifier when available.</summary>
    /// <value>A stable note ID, or <see langword="null"/> for findings not tied to one note.</value>
    public long? NoteId { get; init; } = NoteId;

    /// <summary>Gets the related card identifier when available.</summary>
    /// <value>A stable card ID, or <see langword="null"/> for findings not tied to one card.</value>
    public long? CardId { get; init; } = CardId;

    /// <summary>Gets the related note-type field name when available.</summary>
    /// <value>The exact stored field spelling, or <see langword="null"/>.</value>
    public string? FieldName { get; init; } = FieldName;

    /// <summary>Gets the related media filename when available.</summary>
    /// <value>A collection-relative media filename, or <see langword="null"/>.</value>
    public string? MediaFileName { get; init; } = MediaFileName;

    /// <summary>Gets optional corrective guidance.</summary>
    /// <value>A concise suggested next action, or <see langword="null"/> when no generic remediation is safe.</value>
    public string? SuggestedRemediation { get; init; } = SuggestedRemediation;
}
