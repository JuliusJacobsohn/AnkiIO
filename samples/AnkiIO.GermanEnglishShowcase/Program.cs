using System.Text;
using System.Xml.Linq;
using AnkiIO;
using AnkiIO.GermanEnglishShowcase;

const string DefaultOutput = "artifacts/samples/AnkiIO-German-English-Showcase.apkg";

if (args.Length > 1)
{
    throw new ArgumentException("Pass at most one argument: the output .apkg path.");
}

var outputPath = Path.GetFullPath(args.FirstOrDefault() ?? DefaultOutput);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var deck = await CreateDeckAsync();
VerifySourceDeck(deck);
await AnkiPackageWriter.WriteAsync(deck, outputPath);
var imported = await AnkiPackageReader.ReadAsync(outputPath);
VerifyRoundTrip(deck, imported);

Console.WriteLine($"Created: {outputPath}");
Console.WriteLine("10 notes × 2 custom templates = 20 cards");
Console.WriteLine($"{deck.Traverse().Count()} nested deck nodes | {imported.Media.Files.Count} verified media files");
Console.WriteLine("Includes responsive light/dark styling, browser templates, stable IDs, examples, hints, and hierarchical tags.");

static async Task<AnkiDeck> CreateDeckAsync()
{
    var root = new AnkiDeck(
        "German ↔ English · AnkiIO Showcase",
        AnkiId.FromStableValue("showcase-deck", "root"))
    {
        Description = """
            <h2>German ↔ English · AnkiIO Showcase</h2>
            <p>Ten illustrated A1 vocabulary notes with reciprocal cards, natural examples, grammar, hints, and nested topics.</p>
            <p>Generated entirely with the open-source AnkiIO C# library.</p>
            """,
    };

    var everyday = root.AddSubdeck("Everyday", AnkiId.FromStableValue("showcase-deck", "everyday"));
    var food = everyday.AddSubdeck("Food & Drink", AnkiId.FromStableValue("showcase-deck", "food"));
    var home = everyday.AddSubdeck("At Home", AnkiId.FromStableValue("showcase-deck", "home"));
    var travel = root.AddSubdeck("Travel", AnkiId.FromStableValue("showcase-deck", "travel"));
    var gettingAround = travel.AddSubdeck("Getting Around", AnkiId.FromStableValue("showcase-deck", "getting-around"));
    var conversation = travel.AddSubdeck("Conversation", AnkiId.FromStableValue("showcase-deck", "conversation"));

    food.Description = "Food and drink for a first German conversation.";
    home.Description = "Useful words for things around you.";
    gettingAround.Description = "Transport words for finding your way.";
    conversation.Description = "A descriptive word and a polite survival phrase.";

    var noteType = CreateVocabularyNoteType();
    var destinations = new Dictionary<string, AnkiDeck>(StringComparer.Ordinal)
    {
        ["food"] = food,
        ["home"] = home,
        ["travel"] = gettingAround,
        ["conversation"] = conversation,
    };

    var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "_ankiio.svg");
    await root.Media.AddFileAsync(iconPath);
    foreach (var illustration in Illustrations.All)
    {
        _ = XDocument.Parse(illustration.Value, LoadOptions.None);
        root.Media.AddBytes(illustration.Key, Encoding.UTF8.GetBytes(illustration.Value));
    }

    foreach (var entry in VocabularyData.All)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["German"] = entry.German,
            ["English"] = entry.English,
            ["Pronunciation"] = entry.Pronunciation,
            ["Grammar"] = entry.Grammar,
            ["StyleClass"] = entry.StyleClass,
            ["ExampleGerman"] = entry.ExampleGerman,
            ["ExampleEnglish"] = entry.ExampleEnglish,
            ["Hint"] = entry.Hint,
            ["Picture"] = $"<img class=\"illustration\" src=\"{entry.Picture}\" alt=\"\">",
            ["Category"] = entry.Category,
        };

        var tags = VocabularyData.CommonTags.Concat(entry.Tags);
        destinations[entry.Deck].AddNote(
            noteType,
            fields,
            tags,
            guid: $"ankiio-de-{entry.Key}",
            id: AnkiId.FromStableValue("showcase-note", entry.Key));
    }

    return root;
}

static AnkiNoteType CreateVocabularyNoteType()
{
    var noteType = new AnkiNoteType(
            "AnkiIO · Illustrated German Vocabulary",
            id: AnkiId.FromStableValue("showcase-note-type", "german-vocabulary-v1"))
        .AddConfiguredField(new AnkiField("German", Font: "Arial", FontSize: 30))
        .AddConfiguredField(new AnkiField("English", Font: "Arial", FontSize: 28))
        .AddConfiguredField(new AnkiField("Pronunciation", Font: "Arial", FontSize: 18))
        .AddConfiguredField(new AnkiField("Grammar", IsSticky: true, Font: "Arial", FontSize: 18))
        .AddConfiguredField(new AnkiField("StyleClass", IsSticky: true, Font: "Arial", FontSize: 16))
        .AddConfiguredField(new AnkiField("ExampleGerman", Font: "Arial", FontSize: 20))
        .AddConfiguredField(new AnkiField("ExampleEnglish", Font: "Arial", FontSize: 20))
        .AddConfiguredField(new AnkiField("Hint", Font: "Arial", FontSize: 18))
        .AddConfiguredField(new AnkiField("Picture", Font: "Arial", FontSize: 16))
        .AddConfiguredField(new AnkiField("Category", IsSticky: true, Font: "Arial", FontSize: 16))
        .AddConfiguredTemplate(new AnkiCardTemplate(
            "German → English",
            """
            <main class="card-shell">
              <header class="topbar"><span class="flag" aria-hidden="true"></span><span>DE → EN</span><span class="category">{{Category}}</span></header>
              {{#Picture}}<div class="art">{{Picture}}</div>{{/Picture}}
              <div class="prompt german" lang="de">{{German}}</div>
              {{#Pronunciation}}<div class="pronunciation">{{Pronunciation}}</div>{{/Pronunciation}}
              <div class="instruction">Say it in English</div>
            </main>
            """,
            """
            {{FrontSide}}
            <section id="answer" class="answer-panel">
              <div class="answer english" lang="en">{{English}}</div>
              {{#Grammar}}<div class="grammar-chip {{StyleClass}}">{{Grammar}}</div>{{/Grammar}}
              <div class="example"><strong lang="de">{{ExampleGerman}}</strong><span>{{ExampleEnglish}}</span></div>
              {{#Hint}}<details class="hint"><summary>Memory hook</summary><div>{{Hint}}</div></details>{{/Hint}}
              <footer><img src="_ankiio.svg" alt=""><span>Generated with AnkiIO</span></footer>
            </section>
            """,
            BrowserQuestionFormat: "{{German}}",
            BrowserAnswerFormat: "{{English}} · {{Grammar}}"))
        .AddConfiguredTemplate(new AnkiCardTemplate(
            "English → German",
            """
            <main class="card-shell reverse">
              <header class="topbar"><span class="flag" aria-hidden="true"></span><span>EN → DE</span><span class="category">{{Category}}</span></header>
              <div class="prompt english" lang="en">{{English}}</div>
              <div class="instruction">Give the German expression — include the article for nouns</div>
              {{#Hint}}<details class="hint question-hint"><summary>Need a hint?</summary><div>{{Hint}}</div></details>{{/Hint}}
            </main>
            """,
            """
            {{FrontSide}}
            <section id="answer" class="answer-panel">
              {{#Picture}}<div class="art small">{{Picture}}</div>{{/Picture}}
              <div class="answer german" lang="de">{{German}}</div>
              {{#Pronunciation}}<div class="pronunciation">{{Pronunciation}}</div>{{/Pronunciation}}
              {{#Grammar}}<div class="grammar-chip {{StyleClass}}">{{Grammar}}</div>{{/Grammar}}
              <div class="example"><strong lang="de">{{ExampleGerman}}</strong><span>{{ExampleEnglish}}</span></div>
              <footer><img src="_ankiio.svg" alt=""><span>Generated with AnkiIO</span></footer>
            </section>
            """,
            BrowserQuestionFormat: "{{English}}",
            BrowserAnswerFormat: "{{German}} · {{Grammar}}"));

    noteType.Css = """
        .card {
          margin: 0;
          padding: 24px 14px;
          background: radial-gradient(circle at top, #e0f2fe, #eef2ff 45%, #f8fafc);
          color: #172033;
          font-family: Inter, ui-sans-serif, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
          text-align: center;
        }
        .card-shell, .answer-panel {
          box-sizing: border-box;
          width: min(100%, 640px);
          margin: 0 auto;
          border: 1px solid rgba(37, 99, 235, .16);
          background: rgba(255, 255, 255, .94);
          box-shadow: 0 18px 60px rgba(30, 64, 175, .13);
        }
        .card-shell { border-radius: 28px 28px 10px 10px; overflow: hidden; padding-bottom: 28px; }
        .answer-panel { margin-top: 8px; border-radius: 10px 10px 28px 28px; padding: 24px 30px 20px; }
        .topbar { display: flex; gap: 10px; align-items: center; padding: 13px 18px; background: #10244a; color: #fff; font-size: 12px; font-weight: 800; letter-spacing: .1em; text-transform: uppercase; }
        .category { margin-left: auto; color: #bfdbfe; letter-spacing: .04em; }
        .flag { width: 28px; height: 18px; border-radius: 3px; background: linear-gradient(#171717 0 33%, #dc2626 33% 66%, #fbbf24 66%); box-shadow: 0 0 0 1px rgba(255,255,255,.28); }
        .art { width: 170px; margin: 25px auto 8px; }
        .art.small { width: 112px; margin-top: 0; }
        .illustration { display: block; width: 100%; height: auto; }
        .prompt, .answer { padding: 8px 22px; font-weight: 800; line-height: 1.12; overflow-wrap: anywhere; }
        .prompt { font-size: clamp(32px, 8vw, 52px); }
        .answer { font-size: clamp(30px, 7vw, 46px); color: #1d4ed8; }
        .pronunciation { color: #64748b; font-size: 16px; letter-spacing: .025em; }
        .instruction { margin-top: 20px; color: #64748b; font-size: 12px; font-weight: 800; letter-spacing: .12em; text-transform: uppercase; }
        .grammar-chip { display: inline-block; margin: 15px auto 8px; padding: 7px 12px; border-radius: 999px; background: #e2e8f0; color: #334155; font-size: 13px; font-weight: 750; }
        .grammar-chip.gender-der { background: #dbeafe; color: #1d4ed8; }
        .grammar-chip.gender-die { background: #ffe4e6; color: #be123c; }
        .grammar-chip.gender-das { background: #dcfce7; color: #15803d; }
        .grammar-chip.verb { background: #ede9fe; color: #6d28d9; }
        .grammar-chip.adjective { background: #fae8ff; color: #a21caf; }
        .grammar-chip.phrase { background: #ffedd5; color: #c2410c; }
        .example { margin: 18px auto 8px; padding: 16px 18px; border-left: 4px solid #60a5fa; border-radius: 8px 14px 14px 8px; background: #f8fafc; text-align: left; line-height: 1.45; }
        .example strong, .example span { display: block; }
        .example span { margin-top: 4px; color: #64748b; }
        .hint { margin: 14px auto 0; border-radius: 12px; background: #fff7ed; color: #9a3412; text-align: left; }
        .hint summary { cursor: pointer; padding: 11px 14px; font-weight: 800; }
        .hint div { padding: 0 14px 13px; line-height: 1.4; }
        .question-hint { width: min(86%, 480px); }
        footer { display: flex; justify-content: center; align-items: center; gap: 8px; margin-top: 20px; color: #94a3b8; font-size: 11px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; }
        footer img { width: 24px; height: 24px; border-radius: 6px; }
        .nightMode, .night_mode { background: radial-gradient(circle at top, #172554, #111827 50%, #030712); color: #e5e7eb; }
        .nightMode .card-shell, .nightMode .answer-panel, .night_mode .card-shell, .night_mode .answer-panel { background: rgba(17, 24, 39, .96); border-color: rgba(96, 165, 250, .25); }
        .nightMode .example, .night_mode .example { background: #1f2937; }
        .nightMode .example span, .nightMode .pronunciation, .nightMode .instruction, .night_mode .example span, .night_mode .pronunciation, .night_mode .instruction { color: #9ca3af; }
        @media (max-width: 420px) { .card { padding: 8px 3px; } .answer-panel { padding: 20px 16px 16px; } .category { display: none; } }
        """;

    return noteType;
}

static void VerifySourceDeck(AnkiDeck deck)
{
    var validation = AnkiValidator.Validate(deck);
    if (!validation.IsValid)
    {
        throw new AnkiValidationException(validation);
    }

    var notes = deck.Traverse().SelectMany(value => value.Notes).ToArray();
    Require(notes.Length == 10, $"Expected 10 source notes, found {notes.Length}.");
    Require(notes.Sum(note => note.Cards.Count) == 20, "Expected exactly 20 source cards.");
    Require(notes.Select(note => note.NoteType.Id).Distinct().Count() == 1, "All notes must share one custom note type.");
    Require(notes.All(note => note.Cards.Count == 2), "Every note must generate one card in each direction.");
    Require(notes.All(note => VocabularyData.CommonTags.All(note.Tags.Contains)), "Every note must carry the shared showcase tags.");
    Require(deck.Media.Files.Count == Illustrations.All.Count + 1, "Every generated illustration and the AnkiIO logo must be registered.");
    Require(VocabularyData.All.All(entry => deck.Media.Files.Any(file => file.FileName == entry.Picture)), "Every note illustration must be registered as media.");
}

static void VerifyRoundTrip(AnkiDeck source, AnkiPackage imported)
{
    Require(imported.Decks.Count == 1, $"Expected one root hierarchy, found {imported.Decks.Count}.");
    var expectedDecks = source.Traverse().ToDictionary(deck => deck.Id);
    var actualDecks = imported.Decks[0].Traverse().ToDictionary(deck => deck.Id);
    Require(expectedDecks.Keys.ToHashSet().SetEquals(actualDecks.Keys), "Deck identities changed during round-trip.");
    foreach (var expectedDeck in expectedDecks.Values)
    {
        var actualDeck = actualDecks[expectedDeck.Id];
        Require(expectedDeck.Name == actualDeck.Name, $"Deck {expectedDeck.Id} changed name.");
        Require(expectedDeck.Description == actualDeck.Description, $"Deck '{expectedDeck.Name}' changed description.");
        Require(expectedDeck.Subdecks.Select(deck => deck.Id).ToHashSet().SetEquals(actualDeck.Subdecks.Select(deck => deck.Id)), $"Deck '{expectedDeck.Name}' changed its direct children.");
        Require(expectedDeck.Notes.Select(note => note.Id).ToHashSet().SetEquals(actualDeck.Notes.Select(note => note.Id)), $"Deck '{expectedDeck.Name}' changed its note placement.");
    }

    var expectedNotes = source.Traverse().SelectMany(deck => deck.Notes).ToDictionary(note => note.Id);
    var actualNotes = imported.Notes.ToDictionary(note => note.Id);
    Require(expectedNotes.Keys.ToHashSet().SetEquals(actualNotes.Keys), "Note identities changed during round-trip.");
    Require(actualNotes.Count == 10, $"Expected 10 imported notes, found {actualNotes.Count}.");
    Require(imported.Cards.Count() == 20, $"Expected 20 imported cards, found {imported.Cards.Count()}.");
    foreach (var expectedNote in expectedNotes.Values)
    {
        var actualNote = actualNotes[expectedNote.Id];
        Require(expectedNote.Guid == actualNote.Guid, $"Note {expectedNote.Id} changed GUID.");
        Require(expectedNote.Fields.Count == actualNote.Fields.Count && expectedNote.Fields.All(field => actualNote.Fields.TryGetValue(field.Key, out var value) && value == field.Value), $"Note {expectedNote.Id} changed field content.");
        Require(expectedNote.Tags.SequenceEqual(actualNote.Tags), $"Note {expectedNote.Id} changed tags.");
        Require(expectedNote.NoteType.Id == actualNote.NoteType.Id, $"Note {expectedNote.Id} changed note type identity.");
        Require(expectedNote.NoteType.Name == actualNote.NoteType.Name && expectedNote.NoteType.Kind == actualNote.NoteType.Kind, "The custom note type identity changed.");
        Require(expectedNote.NoteType.Css == actualNote.NoteType.Css, "The custom note type CSS changed.");
        Require(expectedNote.NoteType.Fields.SequenceEqual(actualNote.NoteType.Fields), "The custom field definitions changed.");
        Require(expectedNote.NoteType.Templates.SequenceEqual(actualNote.NoteType.Templates), "The custom card or browser templates changed.");

        var expectedCards = expectedNote.Cards.ToDictionary(card => card.Id);
        var actualCards = actualNote.Cards.ToDictionary(card => card.Id);
        Require(expectedCards.Keys.ToHashSet().SetEquals(actualCards.Keys), $"Note {expectedNote.Id} changed card identities.");
        foreach (var expectedCard in expectedCards.Values)
        {
            var actualCard = actualCards[expectedCard.Id];
            Require(expectedCard.NoteId == actualCard.NoteId && expectedCard.DeckId == actualCard.DeckId && expectedCard.TemplateOrdinal == actualCard.TemplateOrdinal, $"Card {expectedCard.Id} changed its relationship.");
            Require(expectedCard.Scheduling == actualCard.Scheduling && expectedCard.Flag == actualCard.Flag, $"Card {expectedCard.Id} changed scheduling or flag state.");
        }
    }

    Require(actualNotes.Values.All(note => note.NoteType.Templates.Count == 2), "The two custom templates were not preserved.");
    Require(actualNotes.Values.All(note => note.NoteType.Fields.Count == 10), "The custom ten-field note type was not preserved.");
    Require(actualNotes.Values.All(note => VocabularyData.CommonTags.All(note.Tags.Contains)), "Hierarchical tags changed during round-trip.");
    Require(imported.Cards.All(card => card.Scheduling == AnkiScheduling.New), "New-card scheduling changed during round-trip.");
    Require(actualDecks.Count == 7, "The complete seven-node deck hierarchy was not preserved.");
    Require(imported.Diagnostics.All(diagnostic => diagnostic.Severity != AnkiDiagnosticSeverity.Error), "Package read-back produced an error diagnostic.");

    var expectedMedia = source.Media.Files.ToDictionary(file => file.FileName, file => file.Sha256, StringComparer.Ordinal);
    var actualMedia = imported.Media.Files.ToDictionary(file => file.FileName, file => file.Sha256, StringComparer.Ordinal);
    Require(expectedMedia.Count == actualMedia.Count, "The package media count changed during round-trip.");
    Require(expectedMedia.All(pair => actualMedia.TryGetValue(pair.Key, out var hash) && hash == pair.Value), "A media filename or SHA-256 digest changed during round-trip.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal static class VocabularyData
{
    public static IReadOnlyList<string> CommonTags { get; } = ["AnkiIO::Showcase", "Language::German", "Level::A1"];

    public static IReadOnlyList<VocabularyEntry> All { get; } =
    [
        new("apple", "food", "der Apfel", "apple", "/ˈapfl̩/", "Noun · masculine · plural: die Äpfel", "gender-der", "Der Apfel ist rot.", "The apple is red.", "Apfel and apple share the same ancient root.", "ankiio_apple.svg", "Food", ["Topic::Food", "PartOfSpeech::Noun", "Gender::Masculine"]),
        new("bread-roll", "food", "das Brötchen", "bread roll", "/ˈbʁøːtçən/", "Noun · neuter · plural: die Brötchen", "gender-das", "Zum Frühstück esse ich ein Brötchen.", "I eat a bread roll for breakfast.", "The diminutive ending -chen is always neuter: das.", "ankiio_bread_roll.svg", "Food", ["Topic::Food", "PartOfSpeech::Noun", "Gender::Neuter"]),
        new("coffee", "food", "der Kaffee", "coffee", "/kaˈfeː/", "Noun · masculine · usually uncountable", "gender-der", "Möchtest du einen Kaffee?", "Would you like a coffee?", "It looks like coffee; stress the second syllable: ka-FEE.", "ankiio_coffee.svg", "Food", ["Topic::Food", "PartOfSpeech::Noun", "Gender::Masculine"]),
        new("key", "home", "der Schlüssel", "key", "/ˈʃlʏsl̩/", "Noun · masculine · plural: die Schlüssel", "gender-der", "Wo ist mein Schlüssel?", "Where is my key?", "A Schlüssel opens a Schloss — a lock.", "ankiio_key.svg", "Home", ["Topic::Home", "PartOfSpeech::Noun", "Gender::Masculine"]),
        new("door", "home", "die Tür", "door", "/tyːɐ̯/", "Noun · feminine · plural: die Türen", "gender-die", "Bitte mach die Tür zu.", "Please close the door.", "Tür and English door are linguistic cousins.", "ankiio_door.svg", "Home", ["Topic::Home", "PartOfSpeech::Noun", "Gender::Feminine"]),
        new("bicycle", "travel", "das Fahrrad", "bicycle", "/ˈfaːɐ̯ʁaːt/", "Noun · neuter · plural: die Fahrräder", "gender-das", "Ich fahre mit dem Fahrrad zur Arbeit.", "I ride my bike to work.", "Fahr means travel or ride; Rad means wheel.", "ankiio_bicycle.svg", "Transport", ["Topic::Transport", "PartOfSpeech::Noun", "Gender::Neuter"]),
        new("station", "travel", "der Bahnhof", "train station", "/ˈbaːnˌhoːf/", "Noun · masculine · plural: die Bahnhöfe", "gender-der", "Der Bahnhof ist gleich um die Ecke.", "The train station is just around the corner.", "Bahn is railway and Hof is yard: a railway yard.", "ankiio_station.svg", "Transport", ["Topic::Transport", "PartOfSpeech::Noun", "Gender::Masculine"]),
        new("travel", "travel", "fahren", "to travel / go by vehicle", "/ˈfaːʁən/", "Strong verb · fährt · fuhr · ist gefahren", "verb", "Wir fahren morgen nach Berlin.", "We’re traveling to Berlin tomorrow.", "Think of English fare in the sense of making a journey.", "ankiio_train.svg", "Transport", ["Topic::Transport", "PartOfSpeech::Verb", "Verb::Strong"]),
        new("cozy", "conversation", "gemütlich", "cozy; comfortable", "/ɡəˈmyːtlɪç/", "Adjective", "adjective", "Das kleine Café ist sehr gemütlich.", "The little café is very cozy.", "Gemütlich suggests warmth, comfort, and a welcoming atmosphere.", "ankiio_cafe.svg", "Description", ["Topic::Description", "PartOfSpeech::Adjective"]),
        new("excuse-me", "conversation", "Entschuldigung, wo ist …?", "Excuse me, where is …?", "/ɛntˈʃʊldɪɡʊŋ voː ɪst/", "Polite phrase", "phrase", "Entschuldigung, wo ist der Bahnhof?", "Excuse me, where is the train station?", "Use this before politely asking a stranger for directions.", "ankiio_map_pin.svg", "Conversation", ["Topic::Travel", "PartOfSpeech::Phrase", "Register::Polite"]),
    ];
}
