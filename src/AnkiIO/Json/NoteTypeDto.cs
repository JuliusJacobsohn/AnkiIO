namespace AnkiIO;

internal sealed class NoteTypeDto
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public AnkiNoteTypeKind Kind { get; set; }

    public string Css { get; set; } = string.Empty;

    public List<AnkiField>? Fields { get; set; } = [];

    public List<AnkiCardTemplate>? Templates { get; set; } = [];
}
