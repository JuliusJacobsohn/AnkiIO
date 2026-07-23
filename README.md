# AnkiIO

> [!WARNING]
> Created with substantial help from **GPT-5.6-Sol Ultra** and tested against my own use cases. Please do not treat this project as a measure of my abilities as a developer, for better or worse.

[![CI](https://github.com/JuliusJacobsohn/AnkiIO/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/JuliusJacobsohn/AnkiIO/actions/workflows/ci.yml)
[![Documentation](https://github.com/JuliusJacobsohn/AnkiIO/actions/workflows/docs.yml/badge.svg?branch=main)](https://juliusjacobsohn.github.io/AnkiIO/)
[![Security](https://github.com/JuliusJacobsohn/AnkiIO/actions/workflows/security.yml/badge.svg?branch=main)](https://github.com/JuliusJacobsohn/AnkiIO/actions/workflows/security.yml)
[![Coverage gate](https://img.shields.io/badge/coverage-95%25_lines_%7C_85%25_branches-brightgreen)](build/verify-coverage.ps1)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**[Get AnkiIO on NuGet](https://www.nuget.org/packages/AnkiIO)** · **[Browse the API documentation →](https://juliusjacobsohn.github.io/AnkiIO/)**

[Sample projects](samples/README.md) · [Formats and safety](https://juliusjacobsohn.github.io/AnkiIO/formats_and_safety.html) · [MIT license](LICENSE)

AnkiIO is a stable, open-source .NET 10 library for creating, validating, importing, editing, and exporting Anki deck data. It provides a format-independent deck model, practical helpers for common note types, deterministic JSON, a CrowdAnki-inspired interchange format, media, scheduling state, diagnostics, and guarded legacy APKG I/O.

## Quick start

Install the package from [NuGet](https://www.nuget.org/packages/AnkiIO):

```shell
dotnet add package AnkiIO
```

```csharp
using AnkiIO;

var deck = new AnkiDeck("German Vocabulary");
deck.AddBasicNote("Haus", "house", tags: ["german", "vocabulary"]);

await AnkiPackageWriter.WriteAsync(deck, "GermanVocabulary.apkg");
```

A note stores information; a card is a study prompt generated from a note template. Scheduling belongs to cards. New cards receive a safe default schedule, while explicit scheduling is validated and rejected when its queue/type combination is inconsistent.

## Highlights

- Decks and nested `Parent::Child` hierarchies
- Notes, ordered fields, custom note types, card templates, CSS, tags, GUIDs, and cards
- Helpers for Basic, Basic-and-reversed, and Cloze notes, including safe cloze-markup construction
- Safe new-card initialization and explicit, validated scheduling state
- Streaming media ingestion with SHA-256 integrity metadata
- Deterministic, versioned native JSON and CrowdAnki-inspired JSON
- Guarded APKG import/export with configurable archive limits and path-traversal defenses
- Structured validation diagnostics and documented unknown-data preservation
- Read-only installed-Anki detection and opt-in, isolated local compatibility testing

See the [samples](samples/README.md) for focused programs and the [German–English showcase](samples/AnkiIO.GermanEnglishShowcase/README.md) for a complete custom deck with note types, media, tags, and verification.

## Compatibility and scope

AnkiIO targets `net10.0`, collection schema 18, and v3 scheduler semantics. APKG export writes the legacy `collection.anki2` representation. Native JSON and CrowdAnki-inspired JSON support documented subsets, with unknown native-JSON properties retained where the API promises preservation.

AnkiIO does not write to a live Anki profile or generate modern `collection.anki21b` databases. Filtered decks, arbitrary database mutation, and complete review-log or deck-configuration preservation are outside its scope. Read [formats, compatibility, and safety](https://juliusjacobsohn.github.io/AnkiIO/formats_and_safety.html) before processing untrusted packages or important data.

Contributions use focused pull requests and must preserve the documented compatibility contract; see [CONTRIBUTING.md](CONTRIBUTING.md). Security reports follow [SECURITY.md](SECURITY.md). Licensed under [MIT](LICENSE).

AnkiIO is maintained by Julius Jacobsohn.

AnkiIO is not affiliated with, endorsed by, or sponsored by Ankitects, Anki, AnkiDroid, or CrowdAnki. “Anki” is a trademark of Ankitects Pty Ltd.
