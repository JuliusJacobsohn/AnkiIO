namespace AnkiIO;

/// <summary>Represents the semantic content read from an Anki deck package.</summary>
public sealed class AnkiPackage
{
    internal AnkiPackage(IReadOnlyList<AnkiDeck> decks, AnkiMediaCollection media, IReadOnlyList<AnkiDiagnostic> diagnostics)
    {
        Decks = decks;
        Media = media;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets top-level deck hierarchies.</summary>
    public IReadOnlyList<AnkiDeck> Decks { get; }

    /// <summary>Gets media extracted into caller-independent memory.</summary>
    public AnkiMediaCollection Media { get; }

    /// <summary>Gets compatibility and preservation diagnostics produced while reading.</summary>
    public IReadOnlyList<AnkiDiagnostic> Diagnostics { get; }

    /// <summary>Enumerates every note in every hierarchy.</summary>
    public IEnumerable<AnkiNote> Notes => Decks.SelectMany(deck => deck.Traverse()).SelectMany(deck => deck.Notes);

    /// <summary>Enumerates every generated or imported card.</summary>
    public IEnumerable<AnkiCard> Cards => Notes.SelectMany(note => note.Cards);
}

/// <summary>Configures defenses applied to untrusted package archives.</summary>
public sealed record AnkiPackageLimits
{
    /// <summary>Gets the safe default limits.</summary>
    public static AnkiPackageLimits Default { get; } = new();

    /// <summary>Gets the maximum archive entry count.</summary>
    public int MaximumEntries { get; init; } = 10_000;

    /// <summary>Gets the maximum uncompressed size of one entry.</summary>
    public long MaximumEntryBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Gets the maximum total uncompressed archive size.</summary>
    public long MaximumTotalBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Gets the maximum uncompressed-to-compressed ratio for a non-empty entry.</summary>
    public double MaximumCompressionRatio { get; init; } = 200;

    /// <summary>Gets the maximum collection database size.</summary>
    public long MaximumCollectionBytes { get; init; } = 512L * 1024 * 1024;
}

/// <summary>Signals that an input package violates archive safety limits or structure.</summary>
public sealed class AnkiPackageSecurityException : IOException
{
    /// <summary>Initializes a package-security failure.</summary>
    public AnkiPackageSecurityException(string message)
        : base(message)
    {
    }
}
