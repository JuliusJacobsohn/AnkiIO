> [!CAUTION]
> **AI-GENERATED PROJECT NOTICE:** This project was initially generated and developed with substantial assistance from artificial intelligence. All functionality should be independently reviewed, tested, and validated before use with important Anki collections.

# AnkiIO

AnkiIO is an experimental, open-source .NET library for building, validating, and round-tripping Anki deck data. It provides a format-independent domain model, deterministic JSON, a CrowdAnki-inspired interchange adapter, media handling, scheduling models, and guarded package I/O.

AnkiIO is not affiliated with, endorsed by, or sponsored by Ankitects, Anki, AnkiDroid, or CrowdAnki. "Anki" is a trademark of Ankitects Pty Ltd.

## Compatibility target

Development is tied to **Anki 26.05** (build `e64c6b1a`) on Windows 11 (build 10.0.26200), collection schema 18, and the v3 scheduler. Portable builds target .NET 8. The package adapter emits the legacy `collection.anki2` APKG representation accepted in an isolated Anki 26.05 backend test; modern `collection.anki21b` emission is not yet claimed.

See [the compatibility report](docs/compatibility/anki-26.05.md) and [known limitations](docs/known-limitations.md) before using package I/O.

## Install

```shell
dotnet add package AnkiIO --prerelease
```

The package has not been published. Build `0.1.0-alpha.1` locally with `dotnet pack -c Release`.

## Quick start

```csharp
using AnkiIO;

var deck = new AnkiDeck("German Vocabulary");
deck.AddBasicNote("Haus", "house", tags: ["german", "vocabulary"]);

await AnkiPackageWriter.WriteAsync(deck, "GermanVocabulary.apkg");
```

A note stores information; a card is a study prompt generated from a note template. Scheduling belongs to cards. Explicit scheduling is advanced and is rejected when its queue/type combination is inconsistent.

## Features

- Decks and nested `Parent::Child` hierarchies
- Notes, ordered fields, note types, templates, CSS, tags, GUIDs, and cards
- Safe new-card initialization and explicit validated scheduling state
- Streaming media ingestion with SHA-256 integrity metadata
- Deterministic, versioned native JSON and CrowdAnki-inspired JSON
- Guarded APKG import/export with archive limits and path-traversal defenses
- Structured validation diagnostics and unknown JSON metadata preservation
- Read-only installed-Anki detection and opt-in local compatibility testing

Partial: review log and deck-configuration preservation; legacy APKG scheduling round trips. Unsupported: direct writes to a live collection, modern `collection.anki21b` generation, filtered decks, and arbitrary schema-18 database mutation.

## Build and test

```shell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet test --filter Category=LocalAnkiCompatibility
dotnet pack src/AnkiIO/AnkiIO.csproj --configuration Release
./build/build-docs.ps1
```

Local-Anki tests are opt-in and use unique temporary workspaces. They never modify the normal profile. The comprehensive API reference is generated from the compiled public surface and XML comments—generated API metadata is not committed. See [local testing safety](docs/local-anki-testing.md), [samples](samples/README.md), and the [documentation index](docs/index.md).

Versioning follows Semantic Versioning. Contributions use feature branches, Conventional Commits, pull requests, required CI, and squash merging; see [CONTRIBUTING.md](CONTRIBUTING.md). Licensed under [MIT](LICENSE).
