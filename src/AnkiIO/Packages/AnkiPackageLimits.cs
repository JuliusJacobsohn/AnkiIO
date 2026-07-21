namespace AnkiIO;

/// <summary>Defines archive-resource limits enforced before an untrusted Anki package is extracted.</summary>
/// <remarks>
/// <para>
/// The reader checks ZIP entry count, each declared uncompressed length, the sum of declared uncompressed lengths,
/// uncompressed-to-compressed ratio, and the declared collection database length. Bounds are inclusive. A non-empty entry
/// claiming zero compressed bytes is treated as an infinite ratio and rejected.
/// </para>
/// <para>
/// These controls mitigate common ZIP-bomb and oversized-archive attacks; they are not a general sandbox. They do not make
/// malformed ZIP, JSON, or SQLite content valid, bound every SQLite operation, guarantee a particular allocation pattern,
/// or protect against application code that later retains the mutable result indefinitely. Media is eagerly copied into
/// memory, so lower the byte limits for services with a smaller memory budget.
/// </para>
/// <para>
/// Instances are immutable after initialization. Every configured value is validated immediately, including values set by
/// a record <c>with</c> expression. Counts and byte bounds must be positive. The ratio must be finite and positive.
/// </para>
/// </remarks>
/// <example>
/// Restrict an upload endpoint to a 64 MiB archive expansion budget and a 50:1 ratio:
/// <code>
/// var limits = AnkiPackageLimits.Default with
/// {
///     MaximumEntries = 2_000,
///     MaximumEntryBytes = 32L * 1024 * 1024,
///     MaximumTotalBytes = 64L * 1024 * 1024,
///     MaximumCollectionBytes = 32L * 1024 * 1024,
///     MaximumCompressionRatio = 50,
/// };
/// var package = await AnkiPackageReader.ReadAsync(uploadStream, limits);
/// </code>
/// </example>
public sealed record AnkiPackageLimits
{
    private int maximumEntries = 10_000;
    private long maximumEntryBytes = 256L * 1024 * 1024;
    private long maximumTotalBytes = 2L * 1024 * 1024 * 1024;
    private double maximumCompressionRatio = 200;
    private long maximumCollectionBytes = 512L * 1024 * 1024;

    /// <summary>Initializes the documented default archive limits.</summary>
    public AnkiPackageLimits()
    {
    }

    /// <summary>Gets the shared default limits.</summary>
    /// <value>An immutable instance with the values documented on each property.</value>
    /// <remarks>Use a record <c>with</c> expression to derive per-operation limits without changing this instance.</remarks>
    public static AnkiPackageLimits Default { get; } = new();

    /// <summary>Gets the largest permitted number of ZIP entries.</summary>
    /// <value>An inclusive positive bound. The default is 10,000 entries.</value>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is zero or negative.</exception>
    public int MaximumEntries
    {
        get => maximumEntries;
        init => maximumEntries = RequirePositive(value, nameof(MaximumEntries));
    }

    /// <summary>Gets the largest permitted declared uncompressed length of one ZIP entry.</summary>
    /// <value>An inclusive positive byte bound. The default is 256 MiB.</value>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is zero or negative.</exception>
    public long MaximumEntryBytes
    {
        get => maximumEntryBytes;
        init => maximumEntryBytes = RequirePositive(value, nameof(MaximumEntryBytes));
    }

    /// <summary>Gets the largest permitted sum of all declared uncompressed ZIP entry lengths.</summary>
    /// <value>An inclusive positive byte bound. The default is 2 GiB.</value>
    /// <remarks>This aggregate includes the collection database, media map, media payloads, and unsupported entries.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The assigned value is zero or negative.</exception>
    public long MaximumTotalBytes
    {
        get => maximumTotalBytes;
        init => maximumTotalBytes = RequirePositive(value, nameof(MaximumTotalBytes));
    }

    /// <summary>Gets the largest permitted uncompressed-to-compressed ratio for a non-empty ZIP entry.</summary>
    /// <value>An inclusive finite positive ratio. The default is 200.</value>
    /// <remarks>
    /// For example, a value of 50 permits an entry declaring at most 50 uncompressed bytes per compressed byte. A non-empty
    /// entry declaring zero compressed bytes is rejected regardless of this value.
    /// </remarks>
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

    /// <summary>Gets the largest permitted declared uncompressed length of <c>collection.anki2</c>.</summary>
    /// <value>An inclusive positive byte bound. The default is 512 MiB.</value>
    /// <remarks>This database-specific bound is applied in addition to the per-entry and total archive bounds.</remarks>
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
