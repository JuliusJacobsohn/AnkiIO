namespace AnkiIO;

internal sealed class CardDto
{
    public long Id { get; set; }

    public long DeckId { get; set; }

    public int TemplateOrdinal { get; set; }

    public int Flag { get; set; }

    public AnkiScheduling? Scheduling { get; set; } = AnkiScheduling.New;

    public List<AnkiReviewLog>? ReviewHistory { get; set; } = [];
}
