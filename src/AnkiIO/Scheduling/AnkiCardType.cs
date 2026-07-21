namespace AnkiIO;

/// <summary>Identifies the learning phase retained by an Anki card.</summary>
/// <remarks>
/// Values map directly to Anki's persisted card <c>type</c> integers. For an active card, pair New with the New queue,
/// Learning with Learning or DayLearning, Review with Review, and Relearning with Learning or DayLearning. Suspended,
/// buried, and preview queues retain any defined underlying type. <see cref="AnkiValidator"/> checks these combinations
/// but never advances the scheduler.
/// </remarks>
public enum AnkiCardType
{
    /// <summary>A card that has not entered its first learning step; persisted as <c>0</c>.</summary>
    New = 0,

    /// <summary>A card progressing through initial learning steps; persisted as <c>1</c>.</summary>
    Learning = 1,

    /// <summary>A graduated card scheduled at a review interval; persisted as <c>2</c>.</summary>
    Review = 2,

    /// <summary>A lapsed review card progressing through relearning steps; persisted as <c>3</c>.</summary>
    Relearning = 3,
}
