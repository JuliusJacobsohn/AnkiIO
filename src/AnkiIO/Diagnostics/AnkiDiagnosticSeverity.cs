namespace AnkiIO;

/// <summary>Classifies whether a diagnostic is explanatory, lossy, or blocks a validated write.</summary>
/// <remarks>
/// Use severity for presentation and the stable <see cref="AnkiDiagnostic.Code"/> for program logic. Only
/// <see cref="Error"/> makes <see cref="AnkiValidationResult.IsValid"/> false; information and warnings remain important
/// when an import deliberately defaults or drops unsupported data.
/// </remarks>
public enum AnkiDiagnosticSeverity
{
    /// <summary>Describes a compatibility or preservation decision that requires no corrective action.</summary>
    Information,

    /// <summary>Identifies suspicious or lossy content that can still be represented.</summary>
    Warning,

    /// <summary>Identifies invalid content that prevents validated serialization or package creation.</summary>
    Error,
}
