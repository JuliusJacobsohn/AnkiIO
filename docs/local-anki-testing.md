# Local Anki compatibility testing

Local tests are opt-in: `dotnet test --filter Category=LocalAnkiCompatibility`. They require `ANKI_LOCAL_COMPATIBILITY=1`. The harness must copy a synthetic collection/package into a unique temporary data directory, create a second backup copy before any write, pass that directory to Anki, log paths, and clean it unless preservation was explicitly requested.

Never use the default Anki data directory, normal profile, or normal media directory. Never launch migration arguments against real data. A portable semantic round trip is not evidence that the installed Anki accepts a package; acceptance must be reported separately.

