# Media

`AddFileAsync()` streams and hashes a file without retaining the payload in memory. Keep the source unchanged until writing. `AddBytes()` copies generated/test content. Names must be simple filenames; rooted paths, separators, traversal, and content collisions are rejected. APKG output maps numeric archive entries to names through the `media` JSON entry and rechecks hashes while streaming.

