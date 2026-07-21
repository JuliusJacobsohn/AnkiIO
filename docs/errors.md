# Error handling and troubleshooting

Content problems are diagnostics. Malformed files raise `JsonException`, `InvalidDataException`, or `AnkiPackageSecurityException`. Unsupported schema/container versions raise `NotSupportedException`. Cancellation flows through asynchronous operations. Preserve exception details and diagnostics when reporting a synthetic reproduction.

