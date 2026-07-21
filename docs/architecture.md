# Architecture

The domain model has no SQLite dependency in its public surface. Validation operates on the domain. Native and CrowdAnki-inspired serializers map independently. Package adapters translate through a temporary collection database. Compatibility selection is isolated behind `IAnkiVersionAdapter`.

```mermaid
flowchart TD
  API["High-level API"] --> Domain["Deck / note / card domain"]
  Domain --> Validation["Structured validation"]
  Domain --> Native["Native JSON v1"]
  Domain --> Crowd["CrowdAnki-inspired JSON"]
  Domain --> Package["Guarded APKG adapter"]
  Package --> SQLite["Temporary SQLite collection"]
  Package --> Media["Streaming media"]
  Compatibility["Version detector / adapter registry"] --> Package
```

See `docs/adr` for decisions and their tradeoffs.

