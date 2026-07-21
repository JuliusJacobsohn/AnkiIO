# Contributing

AnkiIO is maintained by Julius Jacobsohn and was developed with substantial AI assistance. Disclose substantial generated contributions, verify them as carefully as handwritten changes, and evaluate every contribution on its technical merits.

## Pull requests

Use a feature branch and a focused pull request. Conventional Commit titles and squash merging are preferred. Keep unrelated refactors separate, update the changelog for user-visible changes, and explain compatibility or security tradeoffs in the pull-request description.

Every source-declared class, record, interface, or enum belongs in its own source file named after the type, including non-public implementation types. Preserve existing namespace and folder organization, and do not reformat unrelated files.

## Public API compatibility

The 1.x public API is a compatibility contract. `src/AnkiIO/PublicAPI.Shipped.txt` records that contract, and analyzer diagnostics `RS0016` and `RS0017` fail the build when source and baseline diverge.

For an additive API, run the following after restore, then review every line added to `PublicAPI.Unshipped.txt`:

```shell
dotnet format analyzers src/AnkiIO/AnkiIO.csproj --diagnostics RS0016 --no-restore
```

Do not remove, rename, reorder, or incompatibly change shipped members in a 1.x release. A breaking removal requires explicit major-version approval and the analyzer’s `*REMOVED*` baseline entry. During a release, move approved additions from `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt` in ordinal order and reset the unshipped file to `#nullable enable`.

## Documentation

API documentation has one source of truth: comments in `src/AnkiIO`. Do not merely restate a signature. Explain the Anki concept, common usage, units and value mappings, ownership and mutability, validation rules, compatibility limits, important failure modes, and interactions with related members. Include a concise example for important entry points.

Document summaries, every parameter and return/value, relevant exceptions, cancellation behavior, thread-safety, and format-specific preservation. Put longer Doxygen pages in `Documentation.cs`; never hand-edit generated HTML. Run `./build/build-docs.ps1`; the Release build and strict Doxygen pass reject incomplete or malformed documentation.

## Tests and verification

Every behavior change needs focused tests, including malformed inputs and boundary cases where applicable. Portable coverage must remain at or above 95% line coverage and 85% branch coverage; coverage is a backstop, not a substitute for meaningful assertions.

Run the complete gate before opening a pull request:

```shell
./build/build.ps1
```

This performs locked restore, formatting verification, Release builds, portable tests and coverage checks, sample generation, package validation, a clean package-consumer round trip, and strict Doxygen generation. Local-Anki compatibility tests are opt-in and require a disposable test environment; never point them at a normal profile.

## Test data and safety

Never contribute a real Anki collection, profile name, note content, media file, user identifier, credential, or local absolute path. Compatibility fixtures must be synthetic and state their provenance. Format changes require a design rationale, adversarial-input coverage, round-trip tests, and updated compatibility and security documentation.

Report suspected vulnerabilities privately as described in [SECURITY.md](SECURITY.md).
