# Contributing

This project was initially generated and developed with substantial AI assistance. Contributors must review generated changes as carefully as human-authored changes and must not obscure AI involvement.

Use a feature branch and a focused pull request. Conventional Commit titles and squash merging are preferred. Before opening a pull request, run restore, formatting verification, a Release build, portable tests, documentation checks, and package creation. Update tests, samples, API documentation, compatibility notes, security analysis, and the changelog when affected.

Never contribute a real Anki collection, profile name, note content, media file, user identifier, or local absolute path. Compatibility fixtures must be synthetic and state their provenance. Format changes require an architecture decision record and round-trip tests. Public API changes require XML documentation and compatibility review.

API documentation has one source of truth: the `///` comments on the public code. Document summaries, every parameter and return/value, relevant exceptions, ownership and mutability, cancellation, thread-safety, compatibility limits, and a concise example for important entry points. Do not edit generated API HTML or commit generated reflection metadata. Run `./build/build-docs.ps1`; the Sandcastle build reflects the Release assembly and rejects missing required XML sections.
