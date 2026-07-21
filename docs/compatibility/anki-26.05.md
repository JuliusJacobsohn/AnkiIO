# Anki 26.05 compatibility report

Audit date: 2026-07-21. Application: Anki 26.05 (Windows product version 26.5, build `e64c6b1a`) installed under `%LOCALAPPDATA%\Programs\Anki`. OS: Windows 11 build 10.0.26200. Portable library target: .NET 8; SDK used: 8.0.400.

## Detected formats

- Collection schema: 18.
- Scheduler: v3 active; the historical `sched_ver()` compatibility value is 2.
- Normalized collection tables: cards, col, config, deck_config, decks, fields, graves, notes, notetypes, revlog, tags, and templates.
- Card columns: id, nid, did, ord, mod, usn, type, queue, due, ivl, factor, reps, lapses, left, odue, odid, flags, and data.
- Media convention: sibling `collection.media` directory and `collection.media.db2` index where applicable.
- Compatibility APKG export: `collection.anki2`, `collection.anki21`, and JSON `media`.
- Modern collection package: protobuf `meta` (`08 03` in the observed export), Zstandard-framed `collection.anki21b`, fallback `collection.anki2`, and Zstandard-framed `media`.

## Evidence

Anki's installed Python/backend modules created a brand-new synthetic schema-18 collection in a unique temporary directory. A backup was copied before each write/import. Anki-generated compatibility and modern packages were inspected. Two AnkiIO-generated legacy APKGs were then imported through Anki 26.05's `import_anki_package` backend API into another new collection.

The backend accepted both. Explicit state was preserved as card type 2, suspended queue -1, due 20, interval 10, factor 2500, repetitions 3, lapses 0. A separate new card remained type/queue 0 with zero review values. The four-byte synthetic `pixel.png` payload was extracted to the isolated media directory unchanged. The result collection remained schema 18.

No normal profile database or media file was opened for write. The only inspection of the normal profile was metadata-level presence and a read-only copied database schema audit before the update. All compatibility writes used unique temporary paths, and all audit workspaces were removed after evidence was recorded.

## Matrix

| Capability | Status | Evidence/limit |
|---|---|---|
| Schema-18 collection detection | Supported | Synthetic backend-created collection |
| Direct schema-18 collection write | Unsupported | Backend/protobuf ownership boundary |
| Legacy APKG import | Supported | Semantic reader tests |
| Legacy APKG export | Supported for modeled fields | Accepted by isolated Anki 26.05 backend |
| Modern `collection.anki21b` import/export | Detected only | Zstandard/protobuf adapter not implemented |
| Native JSON v1 | Supported | Deterministic semantic round trips |
| CrowdAnki-inspired JSON | Partial | Hierarchy/model/note subset; no scheduling |
| Media | Supported for legacy APKG | SHA-256 round trip and isolated extraction |
| Scheduling | Partial | Common fields preserved; FSRS memory state not modeled |
| Review history | Partial | Write/domain support; reader expansion pending |
| Deck configuration | Partial | Safe default emitted; arbitrary presets pending |
| Note types/templates/nested decks | Supported subset | Portable and package round trips |
| Unknown native JSON fields | Supported at deck level | `JsonElement` preservation |
| Unknown protobuf/SQLite fields | Unsupported | Fails explicitly rather than discarding silently |

## Repeat validation

After an Anki update, run `ANKI_LOCAL_COMPATIBILITY=1 dotnet test --filter Category=LocalAnkiCompatibility`, regenerate a backend-created synthetic collection/package, compare schema/package entries, and update or add an `IAnkiVersionAdapter`. Never broaden a version range from version numbers alone.
