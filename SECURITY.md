# Security Policy

Supported security fixes target the latest prerelease. Do not file public issues for suspected vulnerabilities; contact the maintainers privately through the repository security-advisory feature.

Treat APKG, JSON, SQLite, and media as untrusted. AnkiIO applies configurable limits, rejects unsafe archive paths and duplicates, and never writes a live profile. These controls reduce risk but do not make imported HTML or media safe to render. Use isolated workspaces and malware scanning for unknown packages.

