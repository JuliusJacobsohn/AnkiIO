namespace AnkiIO;

/// <summary>Identifies the learning phase represented by an Anki card's current scheduling state.</summary>
/// <remarks>
/// These values mirror Anki's persisted card types. A card's <see cref="AnkiScheduling.Type"/> and
/// <see cref="AnkiScheduling.Queue"/> must form a valid pair; use <see cref="AnkiValidator.Validate(AnkiDeck)"/> before export.
/// </remarks>
public enum AnkiCardType
{
    /// <summary>Indicates a card that has not entered its first learning step.</summary>
    New = 0,
    /// <summary>Indicates a card progressing through its initial learning steps.</summary>
    Learning = 1,
    /// <summary>Indicates a graduated card scheduled at a review interval.</summary>
    Review = 2,
    /// <summary>Indicates a lapsed review card progressing through relearning steps.</summary>
    Relearning = 3,
}

/// <summary>Identifies the active, buried, or suspended queue in which Anki stores a card.</summary>
/// <remarks>
/// Queue values determine the units and interpretation of <see cref="AnkiScheduling.Due"/>. Negative values represent
/// inactive cards while retaining the underlying <see cref="AnkiScheduling.Type"/>.
/// </remarks>
public enum AnkiCardQueue
{
    /// <summary>Indicates a card explicitly suspended by the user.</summary>
    Suspended = -1,
    /// <summary>Indicates a card temporarily buried because a sibling card was shown.</summary>
    SiblingBuried = -2,
    /// <summary>Indicates a card temporarily buried by the scheduler or user.</summary>
    SchedulerBuried = -3,
    /// <summary>Indicates the new-card queue, where due commonly represents a display position.</summary>
    New = 0,
    /// <summary>Indicates an intraday learning queue, where due is normally a Unix timestamp in seconds.</summary>
    Learning = 1,
    /// <summary>Indicates the review queue, where due is a collection-relative day number.</summary>
    Review = 2,
    /// <summary>Indicates a day-based learning or relearning queue.</summary>
    DayLearning = 3,
    /// <summary>Indicates the preview queue used by applicable filtered-deck operations.</summary>
    Preview = 4,
}

/// <summary>Represents the current scheduling fields persisted on one Anki card.</summary>
/// <remarks>
/// This immutable value record preserves scheduler state but does not run Anki's scheduler. <see cref="Due"/> has
/// queue-dependent units: new cards use a position, intraday learning cards normally use Unix seconds, and review cards
/// use a collection-relative day number. Construct explicit non-new states only when importing known-good scheduling
/// data, and validate the completed deck before writing it. Use a <c>with</c> expression to derive a modified copy.
/// </remarks>
/// <example>
/// <code>
/// card.Scheduling = new AnkiScheduling
/// {
///     Type = AnkiCardType.Review,
///     Queue = AnkiCardQueue.Review,
///     Due = 20,
///     Interval = 10,
///     EaseFactor = 2500,
///     Repetitions = 3,
/// };
///
/// var validation = AnkiValidator.Validate(deck);
/// </code>
/// </example>
public sealed record AnkiScheduling
{
    /// <summary>Initializes safe default scheduling for a newly generated, unscheduled card.</summary>
    /// <remarks>
    /// The resulting value uses <see cref="AnkiCardType.New"/> and <see cref="AnkiCardQueue.New"/> with all numeric
    /// fields set to zero and <see cref="CustomData"/> set to an empty string.
    /// </remarks>
    public AnkiScheduling()
    {
    }

    /// <summary>Gets a safe state for a card that Anki should schedule as new.</summary>
    /// <value>A reusable immutable value equivalent to <see langword="new"/> <see cref="AnkiScheduling"/>.</value>
    /// <remarks>Because the record is immutable, this cached instance can be shared safely.</remarks>
    public static AnkiScheduling New { get; } = new();

    /// <summary>Gets the scheduler phase.</summary>
    /// <value>The underlying learning phase. The default is <see cref="AnkiCardType.New"/>.</value>
    public AnkiCardType Type { get; init; } = AnkiCardType.New;

    /// <summary>Gets the active queue, including suspended and buried states.</summary>
    /// <value>The persisted queue value. The default is <see cref="AnkiCardQueue.New"/>.</value>
    public AnkiCardQueue Queue { get; init; } = AnkiCardQueue.New;

    /// <summary>Gets the queue-dependent due value.</summary>
    /// <value>
    /// A new-card position, Unix timestamp in seconds, collection-relative day number, or queue-specific preserved value.
    /// The default is zero.
    /// </value>
    public long Due { get; init; }

    /// <summary>Gets the interval in days, or a negative seconds value for intraday learning.</summary>
    /// <value>The persisted interval encoding. New cards must use zero.</value>
    public int Interval { get; init; }

    /// <summary>Gets the ease factor in per-mille units, such as 2500 for 250%.</summary>
    /// <value>The legacy scheduler ease factor, or zero when it is not assigned.</value>
    public int EaseFactor { get; init; }

    /// <summary>Gets the number of answered reviews.</summary>
    /// <value>A non-negative count when the source data is valid. New cards must use zero.</value>
    public int Repetitions { get; init; }

    /// <summary>Gets the number of lapses.</summary>
    /// <value>A non-negative count when the source data is valid. New cards must use zero.</value>
    public int Lapses { get; init; }

    /// <summary>Gets remaining learning-step data in Anki's packed representation.</summary>
    /// <value>An opaque scheduler-specific packed integer retained for round trips.</value>
    public int RemainingSteps { get; init; }

    /// <summary>Gets the original due value used by filtered-deck and rescheduling operations.</summary>
    /// <value>The prior due value, or zero when no original value applies.</value>
    public long OriginalDue { get; init; }

    /// <summary>Gets the original deck identifier, or zero when not moved to a filtered deck.</summary>
    /// <value>The stable deck identifier from which the card was moved, or zero.</value>
    public long OriginalDeckId { get; init; }

    /// <summary>Gets custom scheduler data that must be preserved without interpretation.</summary>
    /// <value>An opaque scheduler-owned string. The default is <see cref="string.Empty"/>.</value>
    /// <remarks>AnkiIO stores this value verbatim and does not attempt to parse FSRS or add-on-specific content.</remarks>
    public string CustomData { get; init; } = string.Empty;
}

/// <summary>Represents one historical answer event without conflating it with current card scheduling state.</summary>
/// <param name="Id">The stable numeric review-log identifier, commonly derived from the answer timestamp.</param>
/// <param name="ReviewedAt">The absolute instant at which the answer was recorded.</param>
/// <param name="Ease">The selected answer button number as stored by Anki.</param>
/// <param name="Interval">The resulting interval: days when positive, or seconds encoded as a negative value.</param>
/// <param name="PreviousInterval">The interval before this answer, using the same encoding as <paramref name="Interval"/>.</param>
/// <param name="EaseFactor">The resulting legacy ease factor in per-mille units, such as 2500 for 250%.</param>
/// <param name="AnswerTime">The non-negative time spent answering the card.</param>
/// <param name="ReviewType">Anki's persisted numeric review-kind code, retained without enum coercion.</param>
/// <remarks>
/// The record is immutable and suitable for loss-conscious round trips. AnkiIO does not recalculate review history or
/// infer current scheduling from these entries. Callers importing raw data are responsible for preserving source order
/// and valid code values; serializers may order entries by <paramref name="Id"/> for deterministic output.
/// </remarks>
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
    /// <summary>Gets the stable numeric review-log identifier.</summary>
    /// <value>The identifier supplied to the primary constructor.</value>
    public long Id { get; init; } = Id;

    /// <summary>Gets the absolute instant at which the answer was recorded.</summary>
    /// <value>The timestamp supplied to the primary constructor, including its offset.</value>
    public DateTimeOffset ReviewedAt { get; init; } = ReviewedAt;

    /// <summary>Gets the answer button number stored by Anki.</summary>
    /// <value>The raw ease selection supplied to the primary constructor.</value>
    public int Ease { get; init; } = Ease;

    /// <summary>Gets the resulting interval after this answer.</summary>
    /// <value>Days when positive, or seconds encoded as a negative value.</value>
    public int Interval { get; init; } = Interval;

    /// <summary>Gets the interval that applied before this answer.</summary>
    /// <value>The prior interval using the same encoding as <see cref="Interval"/>.</value>
    public int PreviousInterval { get; init; } = PreviousInterval;

    /// <summary>Gets the resulting legacy ease factor in per-mille units.</summary>
    /// <value>The ease factor supplied to the primary constructor, such as 2500 for 250%.</value>
    public int EaseFactor { get; init; } = EaseFactor;

    /// <summary>Gets the time spent answering the card.</summary>
    /// <value>The answer duration supplied to the primary constructor.</value>
    public TimeSpan AnswerTime { get; init; } = AnswerTime;

    /// <summary>Gets Anki's persisted numeric review-kind code.</summary>
    /// <value>The raw review type supplied to the primary constructor and retained without enum coercion.</value>
    public int ReviewType { get; init; } = ReviewType;
}
