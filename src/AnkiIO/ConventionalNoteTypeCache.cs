namespace AnkiIO;

internal sealed class ConventionalNoteTypeCache
{
    public AnkiNoteType? Basic { get; set; }

    public AnkiNoteType? BasicAndReversed { get; set; }

    public AnkiNoteType? Cloze { get; set; }

    public void Observe(AnkiNoteType noteType)
    {
        if (Basic is null && AnkiNoteTypes.IsBasic(noteType))
        {
            Basic = noteType;
        }
        else if (BasicAndReversed is null && AnkiNoteTypes.IsBasicAndReversed(noteType))
        {
            BasicAndReversed = noteType;
        }
        else if (Cloze is null && AnkiNoteTypes.IsCloze(noteType))
        {
            Cloze = noteType;
        }
    }

    public void CopyMissingTo(ConventionalNoteTypeCache destination)
    {
        if (Basic is not null)
        {
            destination.Observe(Basic);
        }

        if (BasicAndReversed is not null)
        {
            destination.Observe(BasicAndReversed);
        }

        if (Cloze is not null)
        {
            destination.Observe(Cloze);
        }
    }
}
