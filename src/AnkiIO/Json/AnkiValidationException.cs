namespace AnkiIO;

/// <summary>Stops serialization or package creation when a deck has structured validation errors.</summary>
/// <remarks>
/// Catch this exception when user-authored content may be incomplete, then inspect <see cref="ValidationResult"/> and branch
/// on stable diagnostic codes such as <c>ANKI020</c>. The human-readable <see cref="Exception.Message"/> is intended for logs
/// and only summarizes the number of errors; it is not a parsing contract.
/// <para>
/// The validation result is a fixed snapshot. Correct the mutable deck graph and invoke the operation again to obtain a new
/// result; changing the deck does not rewrite the diagnostics retained by an existing exception.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// try
/// {
///     string json = AnkiJsonSerializer.Serialize(deck);
/// }
/// catch (AnkiValidationException exception)
/// {
///     foreach (AnkiDiagnostic diagnostic in exception.ValidationResult.Diagnostics)
///     {
///         Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
///     }
/// }
/// </code>
/// </example>
public sealed class AnkiValidationException : Exception
{
    /// <summary>Initializes an exception for a completed failed validation pass.</summary>
    /// <param name="validationResult">The immutable diagnostic snapshot that prevented the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="validationResult"/> is <see langword="null"/>.</exception>
    public AnkiValidationException(AnkiValidationResult validationResult)
        : base(CreateMessage(validationResult))
    {
        ValidationResult = validationResult;
    }

    /// <summary>Gets the complete diagnostic snapshot associated with the failed operation.</summary>
    /// <value>The same validation-result instance supplied to the constructor.</value>
    public AnkiValidationResult ValidationResult { get; }

    private static string CreateMessage(AnkiValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);
        var errorCount = validationResult.Diagnostics.Count(value => value.Severity == AnkiDiagnosticSeverity.Error);
        return $"Anki content validation failed with {errorCount} error(s).";
    }
}
