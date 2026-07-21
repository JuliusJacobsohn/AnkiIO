# Roadmap

AnkiIO 1.0 establishes a stable public API for the documented Anki 26.05, native JSON, CrowdAnki-inspired JSON, and guarded legacy APKG scope. Stability means compatible 1.x releases will not remove or incompatibly change public APIs without the normal Semantic Versioning process; it does not broaden the format claims in the README.

## 1.0 release scope

- Stable deck, note, card, note-type, template, media, scheduling, validation, JSON, and package APIs
- Practical Doxygen reference generated directly from source documentation
- Public API compatibility enforcement, deterministic builds, package validation, and clean-consumer testing
- Portable CI on Linux, Windows, and macOS plus opt-in Anki 26.05 acceptance testing
- Guarded `collection.anki2` APKG round trips without live-profile writes

## After 1.0

Future work will be prioritized by evidence and contributor demand rather than assigned speculative version numbers:

- Modern `collection.anki21b` package generation and broader schema-version acceptance
- More complete review-log and deck-configuration preservation
- Broader CrowdAnki interoperability and fixture coverage
- Acceptance evidence across additional supported Anki releases and operating systems
- Performance work backed by representative benchmarks

New format claims require synthetic fixtures, round-trip tests, explicit compatibility documentation, and isolated testing against the affected Anki version.
