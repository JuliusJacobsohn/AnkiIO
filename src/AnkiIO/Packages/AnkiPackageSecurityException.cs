namespace AnkiIO;

/// <summary>Indicates that an untrusted package was deliberately rejected by an AnkiIO archive-safety rule.</summary>
/// <remarks>
/// Catch this type when a caller needs to distinguish a configured security rejection from ordinary malformed data or an
/// environmental I/O failure. Examples include excessive entry count or size, a suspicious compression ratio, duplicate or
/// unsafe ZIP names, symbolic-link entries, and unsafe media-map names. The message is diagnostic text, not a stable value
/// for program logic.
/// <para>
/// Other invalid packages can instead throw <see cref="InvalidDataException"/>,
/// <see cref="System.Text.Json.JsonException"/>, <see cref="Microsoft.Data.Sqlite.SqliteException"/>,
/// <see cref="NotSupportedException"/>, or another <see cref="IOException"/>. This exception therefore means “rejected by
/// a safety policy,” not “every possible malicious input has been detected.”
/// </para>
/// </remarks>
public sealed class AnkiPackageSecurityException : IOException
{
    /// <summary>Initializes a package-security rejection with diagnostic context.</summary>
    /// <param name="message">A non-localized explanation of the limit or structural rule that rejected the package.</param>
    /// <remarks>Use the exception type for control flow; do not parse <paramref name="message"/>.</remarks>
    public AnkiPackageSecurityException(string message)
        : base(message)
    {
    }
}
