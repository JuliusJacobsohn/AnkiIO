namespace AnkiIO;

/// <summary>Describes the scheduler phase associated with a card.</summary>
public enum AnkiCardType
{
    /// <summary>The card has not entered learning.</summary>
    New = 0,
    /// <summary>The card is in initial learning.</summary>
    Learning = 1,
    /// <summary>The card is in review.</summary>
    Review = 2,
    /// <summary>The card is relearning after a lapse.</summary>
    Relearning = 3,
}

/// <summary>Describes the queue in which Anki places a card.</summary>
public enum AnkiCardQueue
{
    /// <summary>User-suspended card.</summary>
    Suspended = -1,
    /// <summary>Sibling-buried card.</summary>
    SiblingBuried = -2,
    /// <summary>Scheduler-buried card.</summary>
    SchedulerBuried = -3,
    /// <summary>New-card queue.</summary>
    New = 0,
    /// <summary>Learning queue with a timestamp due value.</summary>
    Learning = 1,
    /// <summary>Review queue with a day-number due value.</summary>
    Review = 2,
    /// <summary>Day-based learning queue.</summary>
    DayLearning = 3,
    /// <summary>Preview queue.</summary>
    Preview = 4,
}

/// <summary>Represents scheduling fields stored on an Anki card.</summary>
/// <remarks>Due has queue-dependent units. New cards use a position, learning cards may use Unix seconds, and review cards use a collection-relative day number.</remarks>
public sealed record AnkiScheduling
{
    /// <summary>Gets a safe state for a card that Anki should schedule as new.</summary>
    public static AnkiScheduling New { get; } = new();

    /// <summary>Gets the scheduler phase.</summary>
    public AnkiCardType Type { get; init; } = AnkiCardType.New;

    /// <summary>Gets the active queue, including suspended and buried states.</summary>
    public AnkiCardQueue Queue { get; init; } = AnkiCardQueue.New;

    /// <summary>Gets the queue-dependent due value.</summary>
    public long Due { get; init; }

    /// <summary>Gets the interval in days, or a negative seconds value for intraday learning.</summary>
    public int Interval { get; init; }

    /// <summary>Gets the ease factor in per-mille units, such as 2500 for 250%.</summary>
    public int EaseFactor { get; init; }

    /// <summary>Gets the number of answered reviews.</summary>
    public int Repetitions { get; init; }

    /// <summary>Gets the number of lapses.</summary>
    public int Lapses { get; init; }

    /// <summary>Gets remaining learning-step data in Anki's packed representation.</summary>
    public int RemainingSteps { get; init; }

    /// <summary>Gets the original due value used by filtered-deck and rescheduling operations.</summary>
    public long OriginalDue { get; init; }

    /// <summary>Gets the original deck identifier, or zero when not moved to a filtered deck.</summary>
    public long OriginalDeckId { get; init; }

    /// <summary>Gets custom scheduler data that must be preserved without interpretation.</summary>
    public string CustomData { get; init; } = string.Empty;
}

/// <summary>Represents a review-history entry without conflating it with current scheduling state.</summary>
public sealed record AnkiReviewLog(
    long Id,
    DateTimeOffset ReviewedAt,
    int Ease,
    int Interval,
    int PreviousInterval,
    int EaseFactor,
    TimeSpan AnswerTime,
    int ReviewType);

