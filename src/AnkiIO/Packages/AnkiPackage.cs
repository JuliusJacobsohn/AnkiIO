namespace AnkiIO;

/// <summary>Represents the supported semantic content read from one Anki deck package.</summary>
/// <remarks>
/// The package owns no open file, archive, database, or stream resources and does not implement <see cref="IDisposable"/>.
/// Its deck and media models remain mutable and are not safe for concurrent mutation. Media payloads are copied into the
/// package-level <see cref="Media"/> collection; they are not automatically registered in a deck's
/// <see cref="AnkiDeck.Media"/> collection. Pass the complete package to
/// <see cref="AnkiPackageWriter.WriteAsync(AnkiPackage, Stream, CancellationToken)"/> or its path overload to retain
/// package-level media and every top-level hierarchy during a read-modify-write operation.
/// </remarks>
public sealed class AnkiPackage
{
    internal AnkiPackage(IReadOnlyList<AnkiDeck> decks, AnkiMediaCollection media, IReadOnlyList<AnkiDiagnostic> diagnostics)
    {
        Decks = decks;
        Media = media;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the top-level deck hierarchies represented by the supported package data.</summary>
    /// <value>
    /// A read-only collection of mutable deck graphs. The collection itself cannot be changed through this API.
    /// </value>
    public IReadOnlyList<AnkiDeck> Decks { get; }

    /// <summary>Gets media extracted into caller-independent memory.</summary>
    /// <value>
    /// A mutable collection whose payloads no longer depend on the input package stream or archive file.
    /// </value>
    /// <remarks>
    /// Media extraction is eager. This collection may therefore retain memory proportional to the total extracted media
    /// size, and it is separate from every <see cref="AnkiDeck.Media"/> collection in <see cref="Decks"/>. Package writer
    /// overloads that accept this <see cref="AnkiPackage"/> include both this collection and media registered on its decks.
    /// </remarks>
    public AnkiMediaCollection Media { get; }

    /// <summary>Gets compatibility and preservation diagnostics produced while reading.</summary>
    /// <value>A read-only, ordered collection describing non-fatal format limitations and preservation decisions.</value>
    public IReadOnlyList<AnkiDiagnostic> Diagnostics { get; }

    /// <summary>Enumerates every note currently reachable from every top-level hierarchy.</summary>
    /// <value>A lazy enumeration that reflects subsequent mutations to the deck graphs.</value>
    /// <remarks>Do not mutate the hierarchy while an enumeration is in progress.</remarks>
    public IEnumerable<AnkiNote> Notes => Decks.SelectMany(deck => deck.Traverse()).SelectMany(deck => deck.Notes);

    /// <summary>Enumerates every card currently reachable through <see cref="Notes"/>.</summary>
    /// <value>A lazy enumeration that reflects subsequent note and card mutations.</value>
    /// <remarks>Do not mutate notes or their card collections while an enumeration is in progress.</remarks>
    public IEnumerable<AnkiCard> Cards => Notes.SelectMany(note => note.Cards);
}

/// <summary>Configures defenses applied to untrusted package archives.</summary>
/// <remarks>
/// Instances are immutable after initialization and can be copied with a record <c>with</c> expression. Limit values are
/// validated when assigned: every count and byte bound must be positive, and the compression-ratio bound must be finite
/// and positive. These limits reduce common archive-exhaustion risks but do not make malformed ZIP, JSON, or SQLite
/// content valid.
/// </remarks>
public sealed record AnkiPackageLimits
{
    private int maximumEntries = 10_000;
    private long maximumEntryBytes = 256L * 1024 * 1024;
    private long maximumTotalBytes = 2L * 1024 * 1024 * 1024;
    private double maximumCompressionRatio = 200;
    private long maximumCollectionBytes = 512L * 1024 * 1024;

    /// <summary>Initializes package limits with the documented safe defaults.</summary>
    /// <remarks>Customized values are validated by their property initializers and by record <c>with</c> expressions.</remarks>
    public AnkiPackageLimits()
    {
    }

    /// <summary>Gets the shared safe default limits.</summary>
    /// <value>An immutable default instance; use a <c>with</c> expression to customize a value.</value>
    public static AnkiPackageLimits Default { get; } = new();

    /// <summary>Gets the maximum archive entry count.</summary>
    /// <value>The inclusive entry-count bound. The default is 10,000.</value>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is zero or negative.</exception>
    public int MaximumEntries
    {
        get => maximumEntries;
        init => maximumEntries = RequirePositive(value, nameof(MaximumEntries));
    }

    /// <summary>Gets the maximum uncompressed size of one entry.</summary>
    /// <value>The inclusive per-entry bound in bytes. The default is 256 MiB.</value>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is zero or negative.</exception>
    public long MaximumEntryBytes
    {
        get => maximumEntryBytes;
        init => maximumEntryBytes = RequirePositive(value, nameof(MaximumEntryBytes));
    }

    /// <summary>Gets the maximum total uncompressed archive size.</summary>
    /// <value>The inclusive sum of all entry lengths in bytes. The default is 2 GiB.</value>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is zero or negative.</exception>
    public long MaximumTotalBytes
    {
        get => maximumTotalBytes;
        init => maximumTotalBytes = RequirePositive(value, nameof(MaximumTotalBytes));
    }

    /// <summary>Gets the maximum uncompressed-to-compressed ratio for a non-empty entry.</summary>
    /// <value>The inclusive ratio bound. The default is 200.</value>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is non-finite, zero, or negative.</exception>
    public double MaximumCompressionRatio
    {
        get => maximumCompressionRatio;
        init
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumCompressionRatio), value, "The maximum compression ratio must be finite and positive.");
            }

            maximumCompressionRatio = value;
        }
    }

    /// <summary>Gets the maximum collection database size.</summary>
    /// <value>The inclusive uncompressed <c>collection.anki2</c> length in bytes. The default is 512 MiB.</value>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is zero or negative.</exception>
    public long MaximumCollectionBytes
    {
        get => maximumCollectionBytes;
        init => maximumCollectionBytes = RequirePositive(value, nameof(MaximumCollectionBytes));
    }

    internal void Validate()
    {
        _ = RequirePositive(MaximumEntries, nameof(MaximumEntries));
        _ = RequirePositive(MaximumEntryBytes, nameof(MaximumEntryBytes));
        _ = RequirePositive(MaximumTotalBytes, nameof(MaximumTotalBytes));
        _ = RequirePositive(MaximumCollectionBytes, nameof(MaximumCollectionBytes));
        if (!double.IsFinite(MaximumCompressionRatio) || MaximumCompressionRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCompressionRatio), MaximumCompressionRatio, "The maximum compression ratio must be finite and positive.");
        }
    }

    private static int RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The limit must be positive.");
        }

        return value;
    }

    private static long RequirePositive(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The limit must be positive.");
        }

        return value;
    }
}

/// <summary>Signals that an input package violates archive safety limits or structure.</summary>
/// <remarks>
/// This exception identifies a deliberate AnkiIO safety rejection. Other malformed package data may instead produce
/// <see cref="InvalidDataException"/>, <see cref="System.Text.Json.JsonException"/>,
/// <see cref="Microsoft.Data.Sqlite.SqliteException"/>, or an I/O exception.
/// </remarks>
public sealed class AnkiPackageSecurityException : IOException
{
    /// <summary>Initializes a package-security failure.</summary>
    /// <param name="message">A description of the rejected limit or structural condition.</param>
    /// <remarks>The message is intended for diagnostics; callers should use the exception type rather than parse its text.</remarks>
    public AnkiPackageSecurityException(string message)
        : base(message)
    {
    }
}
