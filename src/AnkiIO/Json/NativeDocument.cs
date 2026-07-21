namespace AnkiIO;

internal sealed class NativeDocument
{
    public int FormatVersion { get; set; }

    public string Generator { get; set; } = string.Empty;

    public List<NoteTypeDto>? NoteTypes { get; set; } = [];

    public DeckDto? Deck { get; set; }
}
