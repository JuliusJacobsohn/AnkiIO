using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AnkiIO;

/// <summary>Creates generated or deterministic positive identifiers suitable for Anki domain objects.</summary>
/// <remarks>
/// AnkiIO identifiers are signed 64-bit values. <see cref="New"/> serves newly created in-process objects, while
/// <see cref="FromStableValue"/> supports repeatable imports from an external identity. Neither method queries or mutates
/// an installed Anki profile.
/// </remarks>
public static class AnkiId
{
    private static long last = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000;

    /// <summary>Creates a positive, process-unique identifier for a new deck, note type, note, or card.</summary>
    /// <returns>A value greater than zero that has not previously been returned by this process.</returns>
    /// <remarks>
    /// The method is thread-safe and combines a millisecond timestamp-shaped starting point with an atomic counter. It does
    /// not coordinate across processes or machines; use <see cref="FromStableValue"/> when repeatability is required.
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
    /// ordinal inputs always produce equal IDs across supported platforms. As with any fixed-width hash projection, a
    /// collision is theoretically possible; callers importing adversarial or very large datasets should still validate
    /// identifier uniqueness.
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
