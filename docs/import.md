# Import guide

Read native JSON with `AnkiJsonSerializer.ReadAsync`, CrowdAnki text with `CrowdAnkiJson.Import`, and legacy-representation APKG with `AnkiPackageReader.ReadAsync`. Inspect all diagnostics before using content. Package reading is read-only and extracts its database only to a unique temporary directory.

