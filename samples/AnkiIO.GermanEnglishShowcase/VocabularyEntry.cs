namespace AnkiIO.GermanEnglishShowcase;

internal sealed record VocabularyEntry(
    string Key,
    string Deck,
    string German,
    string English,
    string Pronunciation,
    string Grammar,
    string StyleClass,
    string ExampleGerman,
    string ExampleEnglish,
    string Hint,
    string Picture,
    string Category,
    string[] Tags);
