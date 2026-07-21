# Deck creation and note insertion

Use `AddSubdeck()` for each local segment and `AddNote()` for values keyed by the configured fields. Unknown fields are rejected. Missing fields become empty strings so incomplete content can be inspected and validated. Supply a stable GUID when building repeatable imports.

Never place `::` in a local segment. Direct card construction is an advanced adapter operation; ordinary callers generate cards from templates.

