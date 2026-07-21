using System.Security.Cryptography;

namespace AnkiIO.Samples;

internal static class Scenarios
{
    public static async Task CreateBasicDeckAsync(string[] args)
    {
        var deck = BasicDeck();
        await AnkiPackageWriter.WriteAsync(deck, Output(args, "basic.apkg"));
    }

    public static async Task NestedDecksAsync(string[] args)
    {
        var root = new AnkiDeck("Languages");
        var german = root.AddSubdeck("German");
        german.AddSubdeck("Verbs").AddBasicNote("gehen", "to go");
        root.AddSubdeck("Spanish").AddBasicNote("casa", "house");
        await AnkiPackageWriter.WriteAsync(root, Output(args, "nested.apkg"));
    }

    public static Task InsertNotesAsync(string[] args)
    {
        var deck = new AnkiDeck("Stable notes");
        var note = deck.AddNote(AnkiNoteTypes.CreateBasic(), Fields("Haus", "house"), ["german", "noun"], "stable-import-guid");
        Console.WriteLine($"{note.Guid}: {note.Fields["Front"]}; valid={AnkiValidator.Validate(deck).IsValid}");
        return Task.CompletedTask;
    }

    public static Task InspectCardsAsync(string[] args)
    {
        var note = BasicDeck().Notes.Single();
        foreach (var card in note.Cards) Console.WriteLine($"card={card.Id}, note={card.NoteId}, ordinal={card.TemplateOrdinal}, queue={card.Scheduling.Queue}");
        return Task.CompletedTask;
    }

    public static Task ReversedAsync(string[] args)
    {
        var deck = new AnkiDeck("Two directions");
        var note = deck.AddBasicAndReversedNote("Haus", "house");
        Console.WriteLine($"One note generated {note.Cards.Count} cards.");
        return Task.CompletedTask;
    }

    public static Task ClozeAsync(string[] args)
    {
        var deck = new AnkiDeck("Cloze");
        var note = deck.AddClozeNote($"{AnkiCloze.Wrap("Berlin")} is in {AnkiCloze.Wrap("Germany", 2)}.", "Geography");
        Console.WriteLine(string.Join(",", note.Cards.Select(card => card.TemplateOrdinal)));
        return Task.CompletedTask;
    }

    public static async Task ImagesAsync(string[] args)
    {
        var deck = new AnkiDeck("Images");
        var media = deck.Media.AddBytes("pixel.png", [137, 80, 78, 71]);
        deck.AddNote(AnkiNoteTypes.CreateBasic(), Fields("<img src=\"pixel.png\">", "Synthetic image"));
        var output = Output(args, "image.apkg");
        await AnkiPackageWriter.WriteAsync(deck, output);
        var imported = await AnkiPackageReader.ReadAsync(output);
        Console.WriteLine($"SHA-256 preserved: {media.Sha256 == imported.Media.Files.Single().Sha256}");
    }

    public static async Task AudioAsync(string[] args)
    {
        var deck = new AnkiDeck("Audio");
        deck.Media.AddBytes("tone.mp3", [73, 68, 51]);
        deck.AddNote(AnkiNoteTypes.CreateBasic(), Fields("[sound:tone.mp3]", "Synthetic audio marker"));
        var output = Output(args, "audio.apkg");
        await AnkiPackageWriter.WriteAsync(deck, output);
        Console.WriteLine((await AnkiPackageReader.ReadAsync(output)).Media.Files.Single().FileName);
    }

    public static Task CustomTypeAsync(string[] args)
    {
        var type = new AnkiNoteType("Vocabulary").AddField("Word").AddField("Meaning").AddField("Hint")
            .AddTemplate("Recall", "{{Word}}{{#Hint}}<br>{{Hint}}{{/Hint}}", "{{FrontSide}}<hr>{{Meaning}}");
        type.Css = ".card { font-size: 24px; }";
        var deck = new AnkiDeck("Custom");
        deck.AddNote(type, new Dictionary<string, string> { ["Word"] = "Haus", ["Meaning"] = "house", ["Hint"] = "noun" });
        Console.WriteLine(AnkiValidator.Validate(deck).IsValid);
        return Task.CompletedTask;
    }

    public static async Task ImportJsonAsync(string[] args)
    {
        RequireInput(args);
        await using var input = File.OpenRead(args[0]);
        var deck = await AnkiJsonSerializer.ReadAsync(input);
        await AnkiPackageWriter.WriteAsync(deck, Output(args[1..], "from-json.apkg"));
    }

    public static Task ExportCrowdAnkiAsync(string[] args)
    {
        File.WriteAllText(Output(args, "deck.json"), CrowdAnkiJson.Export(BasicDeck()));
        return Task.CompletedTask;
    }

    public static async Task ReadPackageAsync(string[] args)
    {
        RequireInput(args);
        var package = await AnkiPackageReader.ReadAsync(args[0]);
        Console.WriteLine($"decks={package.Decks.Count}, notes={package.Notes.Count()}, cards={package.Cards.Count()}, media={package.Media.Files.Count}");
    }

    public static async Task ModifyPackageAsync(string[] args)
    {
        RequireInput(args);
        var package = await AnkiPackageReader.ReadAsync(args[0]);
        var note = package.Notes.First();
        note.SetField(note.NoteType.Fields[^1].Name, "Updated answer");
        note.AddTag("reviewed");
        await AnkiPackageWriter.WriteAsync(package.Decks[0], Output(args[1..], "modified.apkg"));
    }

    public static async Task PreserveSchedulingAsync(string[] args)
    {
        var deck = BasicDeck();
        deck.Notes[0].Cards[0].Scheduling = ReviewScheduling();
        await using var first = new MemoryStream();
        await AnkiPackageWriter.WriteAsync(deck, first);
        first.Position = 0;
        var read = await AnkiPackageReader.ReadAsync(first);
        Console.WriteLine(read.Cards.Single().Scheduling == ReviewScheduling());
    }

    public static async Task ExplicitSchedulingAsync(string[] args)
    {
        Console.Error.WriteLine("WARNING: explicit scheduling is advanced and version-sensitive.");
        var deck = BasicDeck();
        deck.Notes[0].Cards[0].Scheduling = ReviewScheduling() with { Queue = AnkiCardQueue.Suspended };
        Console.WriteLine(AnkiValidator.Validate(deck).IsValid);
        await AnkiPackageWriter.WriteAsync(deck, Output(args, "explicit-scheduling.apkg"));
    }

    public static Task NewSchedulingAsync(string[] args)
    {
        var card = BasicDeck().Notes[0].Cards[0];
        Console.WriteLine($"{card.Scheduling.Type}/{card.Scheduling.Queue}: Anki will initialize scheduling after import.");
        return Task.CompletedTask;
    }

    public static Task ValidateAsync(string[] args)
    {
        var type = new AnkiNoteType("Invalid").AddField("Front").AddTemplate("Card", "{{Missing}}", "{{Front}}");
        var deck = new AnkiDeck("Validation");
        deck.AddNote(type, new Dictionary<string, string> { ["Front"] = string.Empty });
        foreach (var diagnostic in AnkiValidator.Validate(deck).Diagnostics) Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message} {diagnostic.SuggestedRemediation}");
        return Task.CompletedTask;
    }

    public static async Task InspectOnlyAsync(string[] args) => await ReadPackageAsync(args);

    public static Task MigrateAsync(string[] args)
    {
        var v1 = AnkiJsonSerializer.Serialize(BasicDeck());
        var current = AnkiJsonSerializer.Deserialize(v1);
        Console.WriteLine($"Migrated native format {AnkiJsonSerializer.CurrentFormatVersion}: {current.Name}");
        return Task.CompletedTask;
    }

    public static Task LocalCompatibilityAsync(string[] args)
    {
        var installation = AnkiInstallationDetector.Detect();
        Console.WriteLine(installation is null ? "Anki not detected." : $"Anki {installation.VersionText} at {installation.InstallationDirectory}. No profile was opened or modified.");
        return Task.CompletedTask;
    }

    private static AnkiDeck BasicDeck()
    {
        var deck = new AnkiDeck("German");
        deck.AddBasicNote("Haus", "house", ["german"]);
        return deck;
    }

    private static Dictionary<string, string> Fields(string front, string back) => new() { ["Front"] = front, ["Back"] = back };
    private static AnkiScheduling ReviewScheduling() => new() { Type = AnkiCardType.Review, Queue = AnkiCardQueue.Review, Due = 20, Interval = 10, EaseFactor = 2500, Repetitions = 3 };
    private static string Output(string[] args, string name) => args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "AnkiIO-" + name);
    private static void RequireInput(string[] args) { if (args.Length == 0) throw new ArgumentException("This scenario requires an input path."); }
}
