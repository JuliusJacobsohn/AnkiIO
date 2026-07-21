# German-English showcase deck

This standalone program creates a polished, importable Anki package and immediately reads it back with AnkiIO to verify its semantic content and every media SHA-256 digest.

It demonstrates:

- one custom ten-field note type shared across a nested deck hierarchy;
- German-to-English and English-to-German templates, producing exactly 20 cards from 10 notes;
- browser-specific template columns and responsive light/dark CSS;
- conditional pronunciation, illustration, grammar, example, and hint sections;
- gender- and part-of-speech-colored grammar chips;
- hierarchical tags such as `Language::German`, `Level::A1`, and `Topic::Transport`;
- stable deck, note-type, and note identifiers;
- a streamed AnkiIO SVG mark plus ten programmatically generated SVG illustrations;
- pre-write validation and post-write APKG/media round-trip verification.

From the repository root:

```powershell
dotnet run --project samples/AnkiIO.GermanEnglishShowcase --configuration Release -- artifacts/samples/AnkiIO-German-English-Showcase.apkg
```

Import the resulting file from `artifacts/samples/AnkiIO-German-English-Showcase.apkg` into Anki. All content and artwork are synthetic and safe to share; the package does not read or modify an installed Anki profile.
