# Package-format compatibility

An APKG is a ZIP archive with collection data and optional media. AnkiIO currently writes/reads `collection.anki2` with legacy JSON metadata and a `media` mapping. The target Anki importer may accept this backward-compatible representation, but that requires an isolated acceptance test.

Modern `collection.anki21b` uses newer backend-controlled representation/compression and is detected but not read or emitted. Direct schema-18 writes are unsupported. The reader rejects unknown formats rather than risking silent loss.

