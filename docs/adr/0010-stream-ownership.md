# ADR 0010: Stream ownership

Public reader/writer stream overloads leave caller streams open. Streams returned from media descriptors are caller-owned. This matches standard .NET composability expectations.

