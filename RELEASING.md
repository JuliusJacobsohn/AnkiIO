# Releasing

Releases are published from `main` with NuGet Trusted Publishing. No API key is stored in GitHub.

1. Add the release notes and comparison link to `CHANGELOG.md`.
2. Set the stable Semantic Version in `Version.props`.
3. Commit both files and push the commit to `main`.

A change to `Version.props` starts the release workflow. It runs the complete package gate, waits for the same commit's Ubuntu and Windows CI jobs, verifies that the version and tag are unused or already bound to that commit, creates the tag, publishes the NuGet package and symbols, and creates the GitHub release with checksums.

Use the workflow's manual dispatch with publishing disabled to build and validate release artifacts without creating a tag or publishing a package. Failed jobs can be retried safely while neither the version nor tag has been claimed. If NuGet already contains the version but no matching tag exists, investigate instead of forcing the workflow past its identity check.
