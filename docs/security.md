# Security guide

Treat every package as hostile. Defaults cap entry count, per-entry size, total size, collection size, and compression ratio. Duplicate paths, rooted paths, separators, traversal names, invalid media names, and missing mapped entries are rejected before semantic import. Temporary paths are uniquely generated and cleaned in `finally` blocks.

The library does not sanitize HTML, execute JavaScript, render media, or validate SQLite internals beyond the supported schema. Run malware scanning and use OS isolation for untrusted files. Never write a live profile.

