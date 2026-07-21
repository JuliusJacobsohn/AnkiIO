using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AnkiIO;

/// <summary>Creates positive 64-bit identifiers for new objects or repeatable external imports.</summary>
/// <remarks>
/// Decks, note types, notes, and cards occupy a shared validation namespace in an AnkiIO object graph. Use <see cref="New"/>
/// for fresh objects and <see cref="FromStableValue"/> when the same external record must receive the same ID on every run.
/// Neither method reserves IDs in another process, examines caller-supplied IDs, or queries an installed Anki profile;
/// always validate the complete graph before output.
/// </remarks>
public static class AnkiId
{
    private static long last = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000;

    /// <summary>Creates a positive, process-unique identifier for a new deck, note type, note, or card.</summary>
    /// <returns>A value greater than zero that has not previously been returned by this process.</returns>
    /// <remarks>
    /// The method is thread-safe and combines a millisecond timestamp-shaped starting point with an atomic counter. Its
    /// guarantee is process-local: another process, an explicit imported ID, or an existing Anki collection could use the
    /// same number. It is also deliberately not repeatable across runs. Use <see cref="FromStableValue"/> for deterministic
    /// imports and let <see cref="AnkiValidator"/> detect collisions within the graph being written.
    /// </remarks>
    /// <example>
    /// <code>
    /// var deck = new AnkiDeck("Imported", id: AnkiId.New());
    /// </code>
    /// </example>
    public static long New() => Interlocked.Increment(ref last);

    /// <summary>Derives a deterministic positive identifier from a caller-controlled namespace and stable value.</summary>
    /// <param name="scope">A non-empty namespace, such as <c>"external-note"</c>, separating unrelated identity domains.</param>
    /// <param name="value">A non-empty stable source identifier whose exact ordinal text is significant.</param>
    /// <returns>A non-zero positive 63-bit identifier.</returns>
    /// <remarks>
    /// The UTF-8 encoding of <paramref name="scope"/>, a null separator, and <paramref name="value"/> is hashed with
    /// SHA-256. The low eight hash bytes are interpreted consistently as little-endian, with the sign bit cleared. Equal
    /// ordinal inputs—including case and whitespace—always produce equal IDs across supported platforms.
    ///
    /// <para>
    /// Use different scopes for different object kinds (for example <c>deck</c>, <c>note</c>, and <c>card</c>) so equal
    /// source keys do not collide across the shared graph namespace. The 63-bit projection has a small but non-zero
    /// collision probability; validation is still required, especially for adversarial or very large imports. This method
    /// maps identity only—it does not create a stable Anki note GUID; pass that separately to <see cref="AnkiNote"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="scope"/> or <paramref name="value"/> is empty or whitespace.</exception>
    /// <example>
    /// <code>
    /// var noteId = AnkiId.FromStableValue("external-note", sourceRecord.Id);
    /// var cardId = AnkiId.FromStableValue("external-card", sourceRecord.Id);
    /// </code>
    /// </example>
    public static long FromStableValue(string scope, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\0" + value));
        return (BinaryPrimitives.ReadInt64LittleEndian(bytes) & long.MaxValue) is var result && result != 0 ? result : 1;
    }
}
