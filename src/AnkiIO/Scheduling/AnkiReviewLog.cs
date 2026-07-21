namespace AnkiIO;

/// <summary>Preserves one historical answer event separately from a card's current scheduling state.</summary>
/// <param name="Id">The stable legacy review-log key, conventionally derived from the answer time in Unix milliseconds.</param>
/// <param name="ReviewedAt">The absolute answer instant, including its original offset.</param>
/// <param name="Ease">The raw answer-button number stored by Anki.</param>
/// <param name="Interval">The resulting interval: positive days or negative seconds.</param>
/// <param name="PreviousInterval">The preceding interval using the same positive-day/negative-second encoding.</param>
/// <param name="EaseFactor">The resulting legacy ease factor in per-mille units.</param>
/// <param name="AnswerTime">How long the answer took; valid authored values are non-negative.</param>
/// <param name="ReviewType">Anki's raw numeric review-kind code.</param>
/// <remarks>
/// Review records are immutable history and do not recalculate <see cref="AnkiCard.Scheduling"/>. Native AnkiIO JSON
/// preserves every property and orders records by <see cref="Id"/> for deterministic output. The legacy package writer
/// stores ID, card ID, ease, intervals, factor, answer time in milliseconds, and review type; it cannot independently encode
/// <see cref="ReviewedAt"/> and the current legacy package reader does not reconstruct review rows. CrowdAnki-style JSON
/// omits review history and reports that loss. Retain native JSON when exact history must round-trip through AnkiIO.
/// </remarks>
/// <example>
/// <code>
/// card.ReviewHistory.Add(new AnkiReviewLog(
///     Id: 1_750_000_000_000,
///     ReviewedAt: DateTimeOffset.FromUnixTimeMilliseconds(1_750_000_000_000),
///     Ease: 3,
///     Interval: 14,
///     PreviousInterval: 7,
///     EaseFactor: 2500,
///     AnswerTime: TimeSpan.FromSeconds(2),
///     ReviewType: 1));
/// </code>
/// </example>
public sealed record AnkiReviewLog(
    long Id,
    DateTimeOffset ReviewedAt,
    int Ease,
    int Interval,
    int PreviousInterval,
    int EaseFactor,
    TimeSpan AnswerTime,
    int ReviewType)
{
    /// <summary>Gets the stable legacy review-log key.</summary>
    /// <value>A caller-supplied integer, conventionally a Unix-millisecond timestamp.</value>
    public long Id { get; init; } = Id;

    /// <summary>Gets the absolute time represented by this answer event.</summary>
    /// <value>A <see cref="DateTimeOffset"/> preserved exactly by native JSON.</value>
    public DateTimeOffset ReviewedAt { get; init; } = ReviewedAt;

    /// <summary>Gets the raw answer-button selection.</summary>
    /// <value>Anki's stored integer code; AnkiIO retains it without enum coercion.</value>
    public int Ease { get; init; } = Ease;

    /// <summary>Gets the interval produced by the answer.</summary>
    /// <value>Positive days or seconds encoded as a negative integer.</value>
    public int Interval { get; init; } = Interval;

    /// <summary>Gets the interval that applied before the answer.</summary>
    /// <value>Positive days or seconds encoded as a negative integer.</value>
    public int PreviousInterval { get; init; } = PreviousInterval;

    /// <summary>Gets the resulting legacy ease factor.</summary>
    /// <value>A per-mille value such as <c>2500</c> for 250%.</value>
    public int EaseFactor { get; init; } = EaseFactor;

    /// <summary>Gets the duration spent answering.</summary>
    /// <value>A non-negative duration; legacy APKG output stores whole milliseconds.</value>
    public TimeSpan AnswerTime { get; init; } = AnswerTime;

    /// <summary>Gets Anki's raw review-kind code.</summary>
    /// <value>An integer retained without interpretation so unknown future or add-on codes survive native JSON.</value>
    public int ReviewType { get; init; } = ReviewType;
}
