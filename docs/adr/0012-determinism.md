# ADR 0012: Deterministic serialization

Native JSON orders types and entities by stable keys, metadata ordinally, and tags ordinally. Formatting and newline policy are fixed. APKG database timestamps prevent byte identity, so APKG determinism is semantic rather than byte-for-byte.
