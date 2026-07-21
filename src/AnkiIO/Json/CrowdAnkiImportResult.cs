namespace AnkiIO;

/// <summary>Returns a CrowdAnki-style import together with explicit information about concepts that could not be recovered.</summary>
/// <param name="Deck">The newly reconstructed, mutable root deck.</param>
/// <param name="Diagnostics">Ordered compatibility and data-loss findings produced during import.</param>
/// <remarks>
/// Import success does not imply a lossless conversion. In particular, <c>CROWD001</c> reports that card IDs, scheduling,
/// flags, and review history were regenerated or omitted, while <c>CROWD002</c> reports media filenames whose sibling file
/// payloads were unavailable to the string-only importer. Inspect <see cref="Diagnostics"/> before exporting
/// <see cref="Deck"/> to another format.
/// <para>
/// The record retains both constructor arguments by reference. The deck remains mutable and is not safe for concurrent
/// mutation; the diagnostics list should be treated as read-only.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// CrowdAnkiImportResult imported = CrowdAnkiJson.Import(json);
/// foreach (AnkiDiagnostic diagnostic in imported.Diagnostics)
/// {
///     Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
/// }
/// </code>
/// </example>
public sealed record CrowdAnkiImportResult(AnkiDeck Deck, IReadOnlyList<AnkiDiagnostic> Diagnostics)
{
    /// <summary>Gets the mutable imported hierarchy.</summary>
    /// <value>The same deck instance supplied to the constructor; no defensive clone is made.</value>
    public AnkiDeck Deck { get; init; } = Deck;

    /// <summary>Gets ordered compatibility and loss diagnostics for this import.</summary>
    /// <value>The same read-only-list reference supplied to the constructor.</value>
    public IReadOnlyList<AnkiDiagnostic> Diagnostics { get; init; } = Diagnostics;
}
