# ADR 0011: Error handling

Inspectable content defects use structured diagnostics. Malformed, unsupported, security-sensitive, or I/O failures use typed exceptions. Important scheduler values are never silently normalized.

