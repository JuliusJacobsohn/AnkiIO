namespace AnkiIO;

/// <summary>Identifies the active, inactive, or preview queue in which Anki stores a card.</summary>
/// <remarks>
/// Queue selection determines how <see cref="AnkiScheduling.Due"/> is interpreted. New uses a display position, Learning
/// normally uses a Unix timestamp in seconds, Review and DayLearning use a collection-relative day number, and special
/// negative or preview queues preserve a queue-specific value without reinterpretation. AnkiIO stores these values but does
/// not compute today's collection day or execute learning steps.
/// </remarks>
public enum AnkiCardQueue
{
    /// <summary>An explicitly suspended card; persisted as <c>-1</c> while retaining its underlying card type.</summary>
    Suspended = -1,

    /// <summary>A card temporarily buried because a sibling was shown; persisted as <c>-2</c>.</summary>
    SiblingBuried = -2,

    /// <summary>A card temporarily buried by the scheduler or user; persisted as <c>-3</c>.</summary>
    SchedulerBuried = -3,

    /// <summary>The new-card queue; <see cref="AnkiScheduling.Due"/> is normally a display position.</summary>
    New = 0,

    /// <summary>The intraday learning queue; <see cref="AnkiScheduling.Due"/> is normally Unix seconds.</summary>
    Learning = 1,

    /// <summary>The graduated review queue; <see cref="AnkiScheduling.Due"/> is a collection-relative day number.</summary>
    Review = 2,

    /// <summary>A day-based learning or relearning queue whose due value is a collection-relative day number.</summary>
    DayLearning = 3,

    /// <summary>A temporary preview queue used by applicable filtered-deck operations; persisted as <c>4</c>.</summary>
    Preview = 4,
}
