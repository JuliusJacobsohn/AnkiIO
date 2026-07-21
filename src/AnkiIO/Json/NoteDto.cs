namespace AnkiIO;

internal sealed class NoteDto
{
    public long Id { get; set; }

    public string Guid { get; set; } = string.Empty;

    public long NoteTypeId { get; set; }

    public List<string>? Fields { get; set; } = [];

    public List<string>? Tags { get; set; } = [];

    public List<CardDto>? Cards { get; set; } = [];
}
