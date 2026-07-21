# Quick start

Reference `AnkiIO`, create a deck and note type, add notes, validate, then choose JSON or APKG output. Package output defaults to new unscheduled cards.

```csharp
var deck = new AnkiDeck("German");
var basic = AnkiNoteTypes.CreateBasic();
deck.AddNote(basic, new Dictionary<string, string> { ["Front"] = "Haus", ["Back"] = "house" });
var validation = AnkiValidator.Validate(deck);
await AnkiPackageWriter.WriteAsync(deck, "German.apkg");
```

Never point AnkiIO at a live profile for writes. Modify an imported in-memory package and write a new output file.

