# Known limitations

- Modern `collection.anki21b` and direct schema-18 collection writes are unsupported.
- APKG acceptance against the newly installed target remains unclaimed until the isolated test succeeds.
- CrowdAnki support is a subset and omits scheduling, model-remapping UI, and personal fields.
- Package media is materialized on read; path-based media streams on write.
- Filtered decks, FSRS memory state, image occlusion internals, and arbitrary protobuf unknowns are not modeled.
- Review-log reading and deck-configuration round trips are partial.

