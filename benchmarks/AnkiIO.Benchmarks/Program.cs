using System.Diagnostics;
using AnkiIO;

var noteCount = args.Length == 0 ? 10_000 : int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
var deck = new AnkiDeck("Benchmark");
var type = AnkiNoteTypes.CreateBasic();
for (var index = 0; index < noteCount; index++)
{
    deck.AddNote(type, new Dictionary<string, string> { ["Front"] = $"front {index}", ["Back"] = $"back {index}" });
}

var clock = Stopwatch.StartNew();
var json = AnkiJsonSerializer.Serialize(deck);
clock.Stop();
Console.WriteLine($"JSON export: notes={noteCount}, bytes={json.Length}, elapsed={clock.Elapsed}");
clock.Restart();
var restored = AnkiJsonSerializer.Deserialize(json);
clock.Stop();
Console.WriteLine($"JSON import: notes={restored.Notes.Count}, elapsed={clock.Elapsed}");

var package = Path.Combine(Path.GetTempPath(), $"AnkiIO-benchmark-{Guid.NewGuid():N}.apkg");
try
{
    clock.Restart();
    await AnkiPackageWriter.WriteAsync(deck, package);
    clock.Stop();
    Console.WriteLine($"Package creation: bytes={new FileInfo(package).Length}, elapsed={clock.Elapsed}");
}
finally
{
    if (File.Exists(package)) File.Delete(package);
}
