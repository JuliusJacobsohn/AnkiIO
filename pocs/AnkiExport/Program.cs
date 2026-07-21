using System.Net;
using System.Security.Cryptography;
using AnkiIO;

var outputDirectory = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine("artifacts", "poc-decks"));
Directory.CreateDirectory(outputDirectory);

var firstPath = Path.Combine(outputDirectory, "daily-language-v1.apkg");
var secondPath = Path.Combine(outputDirectory, "daily-language-v2.apkg");

await AnkiPackageWriter.WriteAsync(CreateDeck("идти; пойти"), firstPath);
await AnkiPackageWriter.WriteAsync(CreateDeck("идти; сходить"), secondPath);

var first = await AnkiPackageReader.ReadAsync(firstPath);
var second = await AnkiPackageReader.ReadAsync(secondPath);
var firstNote = first.Notes.Single();
var secondNote = second.Notes.Single();

Require(firstNote.Guid == secondNote.Guid, "Note GUID changed between exports.");
Require(firstNote.Id == secondNote.Id, "Numeric note ID changed between exports.");
Require(firstNote.Fields["Back"] != secondNote.Fields["Back"], "Corrected content did not change.");
Require(firstNote.Cards.Count == 1 && secondNote.Cards.Count == 1, "Expected one German-front card in each package.");

Console.WriteLine($"guid={firstNote.Guid}");
Console.WriteLine($"note_id={firstNote.Id}");
Console.WriteLine($"v1_sha256={await Sha256Async(firstPath)}");
Console.WriteLine($"v2_sha256={await Sha256Async(secondPath)}");
Console.WriteLine($"v1={firstPath}");
Console.WriteLine($"v2={secondPath}");
Console.WriteLine("round_trip=pass; manual isolated-profile import remains required");

static AnkiDeck CreateDeck(string russian)
{
    const string externalEntryId = "lexeme:gehen:verb:motion";
    var deck = new AnkiDeck(
        "Daily Language",
        AnkiId.FromStableValue("daily-language-deck", "default"));

    var back = string.Join(
        "<br>",
        $"<b>English:</b> {WebUtility.HtmlEncode("to go")}",
        $"<b>Russian:</b> {WebUtility.HtmlEncode(russian)}",
        $"<b>Spanish:</b> {WebUtility.HtmlEncode("ir")}",
        "<hr>",
        WebUtility.HtmlEncode("Wir gehen nachher noch kurz zum Späti."));

    deck.AddBasicNote(
        "gehen",
        back,
        tags: ["daily-language", "verb", "source::german"],
        guid: StableAnkiGuid(externalEntryId),
        id: AnkiId.FromStableValue("daily-language-note", externalEntryId));

    return deck;
}

static string StableAnkiGuid(string externalEntryId)
{
    var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(externalEntryId));
    return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
}

static async Task<string> Sha256Async(string path)
{
    await using var stream = File.OpenRead(path);
    return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
