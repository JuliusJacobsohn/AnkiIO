# Release checklist

- [ ] Version and changelog agree with the tag.
- [ ] AI disclosure appears in README, contributing guide, docs, and release notes.
- [ ] Formatting, analyzers, Release build, samples, and portable tests pass.
- [ ] Merged Cobertura and HTML coverage pass documented thresholds.
- [ ] DocFX conceptual pages and the Sandcastle API reference build with warnings as errors.
- [ ] Sandcastle reports no missing public summary, parameter, return, or value documentation.
- [ ] NUPKG and SNUPKG metadata/content validate.
- [ ] A clean consumer builds, runs, writes, and rereads a package.
- [ ] Exact Anki compatibility report and isolated acceptance evidence are attached.
- [ ] Supported, partial, unsupported, risk, migration, and test evidence are current.
- [ ] No private Anki data or absolute local path is tracked.
- [ ] Checksums are generated and Git status is clean.
- [ ] NuGet publication has explicit authorization and credentials.
