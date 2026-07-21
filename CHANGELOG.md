# Changelog

All notable changes follow Keep a Changelog and Semantic Versioning.

## [0.1.0-alpha.1] - Unreleased

### Added

- Initial AI-assisted implementation targeting Anki 26.05 and .NET 8.
- Domain, validation, JSON, media, compatibility, and guarded package layers.
- High-level helpers for Basic, Basic-and-reversed, and Cloze notes, plus safe cloze-markup construction.
- Typed note-type overloads that retain field editor settings and browser-specific card templates.

### Changed

- API documentation is now generated from the compiled public surface and comprehensive source XML comments with Sandcastle Help File Builder, including member pages and full-text search.

### Fixed

- Native JSON deserialization now preserves field editor metadata and browser-specific card-template formats.

### Safety notice

This release was substantially AI-generated. Independently validate it before use with important data. Direct live-collection mutation and modern-package emission are not supported.
