namespace AnkiIO;

/// <summary>Contains the supported deck graphs, media, and diagnostics read from one Anki package.</summary>
/// <remarks>
/// <para>
/// An <see cref="AnkiPackage"/> is an in-memory result, not an open archive. The reader closes or releases all temporary
/// database resources before returning it, so the package does not implement <see cref="IDisposable"/> and remains usable
/// after the source file or stream is closed.
/// </para>
/// <para>
/// Use the package writer overloads when modifying a package that was read from disk. They write every top-level deck and
/// retain <see cref="Media"/>. Writing only <c>package.Decks[0]</c> intentionally writes one hierarchy and omits media held
/// only by the package. The deck graphs and media collection remain mutable and are not safe for concurrent mutation.
/// </para>
/// <para>
/// Media extraction is eager: payloads are copied into memory while reading. Peak and retained memory can therefore grow
/// with the allowed archive size. Apply appropriately small <see cref="AnkiPackageLimits"/> when inputs are untrusted.
/// </para>
/// </remarks>
/// <example>
/// Read, modify, and write a package without dropping its package-level media:
/// <code>
/// var package = await AnkiPackageReader.ReadAsync("input.apkg");
/// package.Decks[0].AddBasicNote("bonjour", "hello");
/// await AnkiPackageWriter.WriteAsync(package, "output.apkg");
/// </code>
/// </example>
public sealed class AnkiPackage
{
    internal AnkiPackage(IReadOnlyList<AnkiDeck> decks, AnkiMediaCollection media, IReadOnlyList<AnkiDiagnostic> diagnostics)
    {
        Decks = Array.AsReadOnly(decks.ToArray());
        Media = media;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    /// <summary>Gets a fixed collection of the package's top-level deck hierarchies.</summary>
    /// <value>
    /// A read-only snapshot of root references in package order. The collection cannot be resized or replaced, but each
    /// referenced <see cref="AnkiDeck"/> remains mutable.
    /// </value>
    /// <remarks>
    /// A package may contain more than one root. Traverse each root to reach nested decks. The writer overloads accepting
    /// this package preserve all roots; deck overloads write only the supplied root.
    /// </remarks>
    public IReadOnlyList<AnkiDeck> Decks { get; }

    /// <summary>Gets the media payloads eagerly extracted from the archive.</summary>
    /// <value>A mutable collection backed by caller-independent in-memory copies of the extracted payloads.</value>
    /// <remarks>
    /// This collection is separate from every <see cref="AnkiDeck.Media"/> collection. Package writer overloads combine
    /// both locations, coalesce identical filenames and hashes, and reject conflicting same-name content. Removing an item
    /// here before writing intentionally omits it unless a deck registers the same filename.
    /// </remarks>
    public AnkiMediaCollection Media { get; }

    /// <summary>Gets the ordered compatibility and preservation findings produced while reading.</summary>
    /// <value>A read-only snapshot. Diagnostics describe non-fatal adapter choices and data that could not be preserved.</value>
    /// <remarks>Branch on <see cref="AnkiDiagnostic.Code"/> or severity; human-readable messages may evolve.</remarks>
    public IReadOnlyList<AnkiDiagnostic> Diagnostics { get; }

    /// <summary>Enumerates all notes reachable from every top-level hierarchy.</summary>
    /// <value>A lazy depth-first enumeration that reflects later mutations to the deck graphs.</value>
    /// <remarks>Do not mutate a deck or its note collection while this enumeration is in progress.</remarks>
    public IEnumerable<AnkiNote> Notes => Decks.SelectMany(deck => deck.Traverse()).SelectMany(deck => deck.Notes);

    /// <summary>Enumerates all cards reachable through <see cref="Notes"/>.</summary>
    /// <value>A lazy enumeration that reflects later note and card mutations.</value>
    /// <remarks>Do not mutate notes or card collections while this enumeration is in progress.</remarks>
    public IEnumerable<AnkiCard> Cards => Notes.SelectMany(note => note.Cards);
}
