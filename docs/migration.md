# Migration guide

Native JSON begins at version 1. Future migrations should parse into version-specific DTOs, preserve extension data, map to the current domain, validate, and serialize deterministically. New Anki adapters implement `IAnkiVersionAdapter` only after synthetic fixtures and isolated acceptance tests establish collection, package, scheduler, and media behavior.

