# Contributing

AnkiIO was built with substantial AI assistance. Be transparent about substantial generated contributions and review every change on its technical merits.

Use a feature branch and a focused pull request. Conventional Commit titles and squash merging are preferred. Before opening a pull request, run restore, formatting verification, a Release build, portable tests, documentation checks, and package creation. Update tests, samples, API documentation, compatibility notes, security analysis, and the changelog when affected.

Never contribute a real Anki collection, profile name, note content, media file, user identifier, or local absolute path. Compatibility fixtures must be synthetic and state their provenance. Format changes require an explicit design rationale and round-trip tests. Public API changes require XML documentation and compatibility review.

API documentation has one source of truth: comments in `src/AnkiIO`. Document summaries, every parameter and return/value, relevant exceptions, ownership and mutability, cancellation, thread-safety, compatibility limits, and a concise example for important entry points. Put longer Doxygen pages in `Documentation.cs`; do not hand-edit generated HTML. Run `./build/build-docs.ps1`; the Release build and strict Doxygen pass reject incomplete or malformed documentation.
