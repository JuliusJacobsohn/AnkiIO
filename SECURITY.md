# Security Policy

## Supported versions

Security fixes are provided for the latest stable 1.x release. Pre-1.0 builds and older 1.x releases should be upgraded before reporting a defect that is already fixed in the latest release.

| Version | Supported |
| --- | --- |
| Latest 1.x | Yes |
| Older 1.x | Upgrade required |
| Earlier than 1.0 | No |

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability. Use the repository’s private **Security → Report a vulnerability** feature and include:

- the affected AnkiIO version and commit, operating system, and .NET runtime;
- the smallest synthetic input that reproduces the problem;
- the expected impact and whether the input must be trusted or user-controlled; and
- any known workaround.

Never submit a real Anki collection, profile, note content, media file, API key, or personal path. If a minimal reproducer cannot be safely attached, describe how a maintainer can construct equivalent synthetic data.

## Security boundary

Treat APKG, JSON, SQLite, HTML, and media as untrusted. AnkiIO applies configurable archive limits, rejects duplicate or unsafe archive paths, validates supported database state, and does not write a live profile. Those controls reduce parser and extraction risk; they do not make imported HTML, scripts, audio, images, or other media safe to render.

Use isolated workspaces, conservative `AnkiPackageLimits`, and malware scanning for unknown packages. Review validation diagnostics before export and keep backups of important collections. Direct live-collection mutation and modern `collection.anki21b` emission are not supported security boundaries.
