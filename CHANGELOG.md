# Changelog

All notable changes to AnkiIO are documented here. The project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2026-07-23

This is the first stable release. Its compatibility promise covers the documented Anki 26.05 and legacy APKG scope; it does not imply support for every Anki storage format or add-on extension.

### Added

- A format-independent model for decks, nested subdecks, notes, cards, custom note types, templates, media, and scheduling state.
- High-level helpers for Basic, Basic-and-reversed, and Cloze notes, plus safe cloze-markup construction.
- Deterministic, versioned native JSON and a documented CrowdAnki-inspired interchange subset.
- Guarded legacy APKG import and export with configurable size, entry-count, compression-ratio, duplicate-name, and path-safety limits.
- Structured validation diagnostics, unknown native-JSON metadata preservation, installed-Anki detection, and isolated local compatibility tests.
- A complete German–English showcase deck and focused sample programs.
- A tracked public API compatibility baseline for the stable 1.x surface.

### Changed

- Raised the library and all supported projects to .NET 10 (`net10.0`).
- Replaced the earlier documentation stacks with a single strict Doxygen build generated from source comments and source-resident guides.
- Expanded public API documentation to explain ownership, units, Anki concepts, valid combinations, compatibility limits, exceptions, and practical usage.
- Organized public top-level types into one source file per type.
- Hardened note-type freezing, collection mutability boundaries, cloze reconciliation, tag validation, and stable domain invariants.
- Added atomic path-based package writes and package-level read/write overloads for multi-root decks and package media.
- Raised the portable test coverage gate to 95% line coverage and 85% branch coverage.

### Fixed

- Native JSON round trips retain field editor metadata, browser-specific template formats, and documented unknown properties.
- Package reads reject malformed archives and media maps consistently, including non-finite limits and zero-length compression-ratio edge cases.
- Media registration rejects unsafe portable names and conflicting identities while preserving deterministic lookups.
- Doxygen now emits all C# record types and constrains the project logo to the navigation header.

### Security

- APKG inputs are treated as untrusted archives and are checked before extraction or database access.
- Package writes use a same-directory temporary file followed by atomic replacement when a path overload is used.
- Updated the SQLite provider and explicitly selected the maintained SQLitePCLRaw 3.x native bundle to remove the vulnerable legacy native dependency.
- Direct writes to live Anki profiles remain unsupported.

[1.0.1]: https://github.com/JuliusJacobsohn/AnkiIO/releases/tag/v1.0.1
