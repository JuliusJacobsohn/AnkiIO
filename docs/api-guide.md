# Public API guide

Use `AnkiDeck`, `AnkiNoteType`, and `AnkiNoteTypes` to create content. `AnkiValidator` returns inspectable diagnostics. `AnkiJsonSerializer` is the loss-minimizing supported interchange format. `CrowdAnkiJson` is a documented subset. `AnkiPackageReader` and `AnkiPackageWriter` handle legacy-representation APKG files with safety limits. `AnkiInstallationDetector` never starts Anki.

Stream overloads leave caller streams open. Path-based media remains caller-owned and must remain unchanged until writing; package writers verify SHA-256 again.

