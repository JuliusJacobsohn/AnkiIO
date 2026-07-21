# Release process

Update the changelog and compatibility report; run formatting, Release build, portable/integration tests, coverage gates, docs, pack, package validation, and a clean consumer test. Run opt-in local acceptance on a safe host. Record exact versions, tests, coverage, partial/unsupported features, risks, and AI disclosure.

The release workflow must be dispatched from `main`. It derives the package version from the project, requires the requested tag to be exactly `v{PackageVersion}`, and binds that tag to the workflow commit before any NuGet publication. A requested publication fails during preflight when `NUGET_API_KEY` is unavailable, and an existing package version is treated as an error rather than silently skipped. Dry runs leave both GitHub and NuGet unchanged.
