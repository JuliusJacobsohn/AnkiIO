# Native JSON v1 specification

The root has `formatVersion`, `generator`, ordered `noteTypes`, and one `deck`. Note types store IDs, name, kind, CSS, ordered fields, and ordered templates. Decks store ID, local name, description, metadata, ordered notes, and ordered subdecks. Notes store ID, GUID, note-type ID, ordered values, sorted tags, and cards. Cards include deck/ordinal/flag, scheduling, and review history.

Serialization uses UTF-8-compatible JSON, invariant numeric forms, ordinal ordering, and a trailing newline for the string API. Unknown deck properties are retained in `UnknownData`. Unsupported versions fail rather than being guessed.

