namespace AnkiIO;

/// <summary>Captures the ordered diagnostics and write/no-write decision from one validation pass.</summary>
/// <remarks>
/// A result is an immutable snapshot: its diagnostics cannot be changed through the returned collection and later deck
/// mutations do not recalculate it. Warnings and information do not make a deck invalid. Run
/// <see cref="AnkiValidator.Validate(AnkiDeck)"/> (or its multi-root overload) again after correcting the graph instead of
/// reusing an old result.
/// </remarks>
public sealed class AnkiValidationResult
{
    internal AnkiValidationResult(IReadOnlyList<AnkiDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    /// <summary>Gets findings in deterministic hierarchy and card traversal order.</summary>
    /// <value>A fixed read-only snapshot; it is empty when validation found no issue.</value>
    public IReadOnlyList<AnkiDiagnostic> Diagnostics { get; }

    /// <summary>Gets whether validated serialization and package creation may proceed.</summary>
    /// <value><see langword="true"/> when no diagnostic has <see cref="AnkiDiagnosticSeverity.Error"/> severity.</value>
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != AnkiDiagnosticSeverity.Error);
}
