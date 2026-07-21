namespace AnkiIO;

/// <summary>Preserves the current scheduler state stored on one card without running Anki's scheduler.</summary>
/// <remarks>
/// This immutable record is a storage model. Assign a new value (or a <c>with</c> copy) to
/// <see cref="AnkiCard.Scheduling"/> when state changes, then validate the complete deck. Queue-dependent units are crucial:
/// New due values are positions, intraday Learning due values are normally Unix seconds, and Review or DayLearning due
/// values are collection-relative day numbers. Positive intervals represent days while negative learning intervals encode
/// seconds.
/// <para>
/// Native AnkiIO JSON preserves every property. The supported legacy APKG adapter preserves these current card columns, but
/// CrowdAnki-style JSON intentionally regenerates cards as new and reports <c>CROWD001</c>. Custom data is opaque; AnkiIO
/// does not parse FSRS or add-on-owned payloads.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// card.Scheduling = new AnkiScheduling
/// {
///     Type = AnkiCardType.Review,
///     Queue = AnkiCardQueue.Review,
///     Due = 250,          // collection-relative day
///     Interval = 14,     // days
///     EaseFactor = 2500, // 250%
///     Repetitions = 6,
/// };
/// AnkiValidationResult validation = AnkiValidator.Validate(deck);
/// </code>
/// </example>
public sealed record AnkiScheduling
{
    /// <summary>Initializes safe unscheduled state for a newly generated card.</summary>
    /// <remarks>Type and queue are New, all numeric values are zero, and custom data is empty.</remarks>
    public AnkiScheduling()
    {
    }

    /// <summary>Gets a reusable safe state for a newly generated card.</summary>
    /// <value>An immutable New/New value with zero counters and no custom data.</value>
    public static AnkiScheduling New { get; } = new();

    /// <summary>Gets the underlying learning phase retained by the card.</summary>
    /// <value>One of the four defined <see cref="AnkiCardType"/> values; the default is New.</value>
    public AnkiCardType Type { get; init; } = AnkiCardType.New;

    /// <summary>Gets the active, inactive, or preview scheduler queue.</summary>
    /// <value>A queue compatible with <see cref="Type"/>; the default is New.</value>
    public AnkiCardQueue Queue { get; init; } = AnkiCardQueue.New;

    /// <summary>Gets the queue-dependent due value.</summary>
    /// <value>A new-card position, Unix timestamp in seconds, collection-relative day, or preserved special-queue value.</value>
    public long Due { get; init; }

    /// <summary>Gets the current interval encoding.</summary>
    /// <value>Days when positive, seconds encoded as a negative value for intraday learning, or zero when unassigned.</value>
    public int Interval { get; init; }

    /// <summary>Gets the legacy ease factor in per-mille units.</summary>
    /// <value>For example, <c>2500</c> represents 250%; zero means no factor has been assigned.</value>
    public int EaseFactor { get; init; }

    /// <summary>Gets how many answers have been recorded for the card.</summary>
    /// <value>A non-negative count; a New card must use zero.</value>
    public int Repetitions { get; init; }

    /// <summary>Gets how many recorded answers caused the card to lapse.</summary>
    /// <value>A non-negative count; a New card must use zero.</value>
    public int Lapses { get; init; }

    /// <summary>Gets Anki's packed remaining-learning-step value.</summary>
    /// <value>An opaque scheduler-specific integer retained verbatim rather than interpreted by AnkiIO.</value>
    public int RemainingSteps { get; init; }

    /// <summary>Gets the due value saved before a filtered-deck or rescheduling operation.</summary>
    /// <value>The original queue-dependent due value, or zero when no original value applies.</value>
    public long OriginalDue { get; init; }

    /// <summary>Gets the deck from which the card was moved into a filtered deck.</summary>
    /// <value>The original stable deck ID, or zero when the card was not moved.</value>
    public long OriginalDeckId { get; init; }

    /// <summary>Gets opaque scheduler- or add-on-owned data.</summary>
    /// <value>A verbatim string, commonly empty; AnkiIO does not parse or normalize it.</value>
    public string CustomData { get; init; } = string.Empty;
}
