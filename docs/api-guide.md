# Public API guide

For ordinary cards, start with `AnkiDeck.AddBasicNote`, `AddBasicAndReversedNote`, and `AddClozeNote`. `AnkiCloze.Wrap` creates cloze markup without hand-building delimiters. Use `AnkiNoteType` and `AnkiNoteTypes` when you need custom fields, templates, or CSS. `AnkiValidator` returns inspectable diagnostics. `AnkiJsonSerializer` is the loss-minimizing supported interchange format. `CrowdAnkiJson` is a documented subset. `AnkiPackageReader` and `AnkiPackageWriter` handle legacy-representation APKG files with safety limits. `AnkiInstallationDetector` never starts Anki.

Stream overloads leave caller streams open. Path-based media remains caller-owned and must remain unchanged until writing; package writers verify SHA-256 again.

## Generated API reference

The [complete API reference](api/index.html) is built by Sandcastle Help File Builder from the Release assembly and its compiler-generated XML documentation file. Public signatures, parameters, return types, enum values, and inheritance come from the compiled assembly; descriptions, constraints, exceptions, examples, ownership, and cancellation behavior come from `///` comments beside the code. No generated API YAML is checked into Git.

Run `./build/build-docs.ps1` from the repository root. The build fails when DocFX reports a conceptual-documentation warning, Sandcastle cannot resolve the compiled API, or a public API is missing required summary, parameter, return, or value documentation. The resulting site is under `artifacts/docs`, with the Sandcastle reference under `artifacts/docs/api`.
