# Samples

The `AnkiIO.Samples` console project compiles all twenty focused scenarios. Run one with `dotnet run --project samples/AnkiIO.Samples -- <number> [input]`. Each scenario directory has its own README explaining the Anki concept. Outputs use the OS temporary directory unless an input/output path is supplied.

For a complete, visually polished example, run [`AnkiIO.GermanEnglishShowcase`](AnkiIO.GermanEnglishShowcase/README.md). Its standalone generator creates and reads back an illustrated German-English APKG containing exactly 20 cards, a custom note type, nested decks, tags, and bundled media.
