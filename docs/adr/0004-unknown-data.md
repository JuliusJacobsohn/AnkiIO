# ADR 0004: Unknown data

Native JSON preserves unknown deck properties as cloned `JsonElement` values. Unsupported SQLite/protobuf structures fail explicitly because pretending to preserve opaque relational data would be unsafe.

