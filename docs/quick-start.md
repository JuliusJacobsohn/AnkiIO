# Quick start

Reference `AnkiIO`, create a deck, add notes, validate, then choose JSON or APKG output. The convenience methods reuse conventional note types throughout a deck hierarchy. Package output defaults to new unscheduled cards.

```csharp
var deck = new AnkiDeck("German");
deck.AddBasicNote("Haus", "house", tags: ["noun"]);
deck.AddBasicAndReversedNote("gehen", "to go", tags: ["verb"]);
deck.AddClozeNote($"Germany's capital is {AnkiCloze.Wrap("Berlin", hint: "city")}.");

var validation = AnkiValidator.Validate(deck);
await AnkiPackageWriter.WriteAsync(deck, "German.apkg");
```

Never point AnkiIO at a live profile for writes. Modify an imported in-memory package and write a new output file.
