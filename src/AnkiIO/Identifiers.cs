using System.Security.Cryptography;
using System.Text;

namespace AnkiIO;

/// <summary>Creates stable identifiers without exposing Anki's storage implementation.</summary>
public static class AnkiId
{
    private static long last = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000;

    /// <summary>Returns a process-unique, positive 64-bit identifier suitable for new deck, note, and card records.</summary>
    public static long New() => Interlocked.Increment(ref last);

    /// <summary>Creates a deterministic positive identifier from a caller-controlled namespace and value.</summary>
    /// <param name="scope">A namespace that prevents unrelated values from colliding.</param>
    /// <param name="value">The stable source value.</param>
    /// <returns>A non-zero positive 63-bit identifier.</returns>
    public static long FromStableValue(string scope, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\0" + value));
        return (BitConverter.ToInt64(bytes) & long.MaxValue) is var result && result != 0 ? result : 1;
    }
}

