# Initial bottleneck research

Date: 2026-07-22

This is a living experiment log, not a production architecture decision record.

## Highest-risk bottlenecks

| Risk | Why it matters | PoC/evidence | Current mitigation |
| --- | --- | --- | --- |
| Browser auto-start | Mobile browsers may reject `getUserMedia` on page load without a fresh user gesture, defeating the action-button flow. | `CaptureWeb` logs opt-in page-load attempts, permission delay, display mode, and failure names on the exact device. | Design for one prominent tap until the target phone proves zero-tap behavior. |
| Sequential API latency | Upload + transcription + structured analysis serializes two network/model calls before all-language output is ready. | `AudioPipeline full` prints separate TTS fixture, STT, and analysis latency plus request IDs/usage. | Show preferred translation first; background the full schema. Evaluate streaming only after bounded upload measurements. |
| Realtime complexity | WebRTC/session credentials, event handling, reconnects, and one target language per translation session may cost more engineering than they save for 3–10 second clips. | Realtime translation docs plus later phone experiment. The first PoC deliberately establishes a bounded-upload baseline. | Keep an upload/transcribe fallback and promote Realtime only if measured p95 improvement is material. |
| Model tier mismatch | The flagship can add latency/cost; the smallest tier may mishandle Russian nuance, morphology, or household context. | `benchmark-text` runs identical strict-schema input on Luna, Terra, and Sol. | Route by role based on evals. Start production consideration with Terra, not a blanket Sol default. |
| Schema cold start | Strict JSON schemas can incur first-request processing/caching latency, particularly when schemas change. | Run each model twice and compare first/subsequent latency; keep the schema byte-for-byte stable. | Version a small fixed schema rather than generating per-user schemas. |
| Glossary size and matching | Injecting an entire growing household glossary wastes tokens and can bias unrelated translations. | The sample contains a single private term and requires `glossaryApplied`; later benchmark glossary sizes and false matches. | Deterministic exact/alias shortlist before model calls; pass glossary as data and record revisions. |
| Deduplication semantics | Lemma + POS still merges homonyms and splits idioms/aspect pairs incorrectly. | Add fixed linguistic fixtures before database work. | Store occurrences and sense-review state; enforce uniqueness only on a deliberately versioned canonical key. |
| Anki re-import semantics | Stable GUIDs do not by themselves guarantee the desired update behavior across Anki versions and import options. | Existing isolated compatibility harness plus a future product-level export/re-import test using the local AnkiIO reference. | Keep deck export behind an adapter and gate release on real isolated-profile import tests. |
| Audio persistence | Keeping audio in request memory or a relational BLOB can amplify memory and backup costs. | `CaptureWeb` streams multipart input to a `.partial` file, moves only after success, and computes a checksum. | Stream to private object/file storage; store only metadata in the database. |
| Browser codec compatibility | A browser-supported recording MIME type is not automatically accepted by the downstream transcription endpoint. | `CaptureWeb` negotiates only WebM/Opus or MP4, both documented transcription inputs, and rejects unsupported MIME types server-side. | Keep capture and provider allowlists aligned; transcode only if a target browser lacks a common format. |
| User-edit/reprocess races | A background retry can overwrite a correction made while the job is running. | To be tested when persistence PoC exists. | Immutable revisions, optimistic concurrency, protected user-authored fields, and apply-results transaction. |

## Confirmed constraints from current OpenAI documentation

- File transcription accepts common mobile formats including WebM and is limited to 25 MB per upload.
- `gpt-4o-transcribe` and `gpt-4o-mini-transcribe` can use a prompt, which is useful for private terms and uncommon names.
- The legacy audio translation endpoint translates only to English and therefore cannot satisfy Russian/Spanish requirements.
- Structured Outputs should use a strict schema with all properties required and `additionalProperties: false`.
- Strict schema processing can add latency on a schema's first request, making a stable versioned schema important.
- Realtime model input transcription is asynchronous guidance and is not necessarily the exact representation understood by the audio model.
- `gpt-realtime-translate` is optimized for live translation but takes one target output language per session.
- GPT-5.6 defaults to medium reasoning. The PoC sets `reasoning.effort` to `none` explicitly for a latency baseline.

Sources:

- [Speech to text](https://developers.openai.com/api/docs/guides/speech-to-text)
- [Structured model outputs](https://developers.openai.com/api/docs/guides/structured-outputs)
- [Realtime and audio](https://developers.openai.com/api/docs/guides/realtime)
- [Realtime translation guide](https://developers.openai.com/cookbook/examples/voice_solutions/realtime_translation_guide)
- [GPT-5.6 prompting guidance](https://developers.openai.com/api/docs/guides/prompt-guidance-gpt-5p6)

## Experiment protocol

For latency comparisons, run at least one warm-up followed by five measured requests per model/path. Record p50 and p95 rather than selecting architecture from a single call. Use the same audio, transcript, prompt, schema bytes, region, and network. Treat synthetic TTS as plumbing evidence only; quality evaluation requires consented real casual speech from the intended users.

For quality, build a small labeled fixture set before tuning prompts. It should include fillers, false starts, code-switching, separable/reflexive verbs, compound nouns, colloquial contractions, profanity/register, Russian aspect and stress, Spanish gender, homonyms, idioms, and private glossary terms. Score transcript correctness, sentence meaning, register, lemma/POS accuracy, glossary use, schema validity, and unwanted vocabulary.

## Measurements

Populate this section after each reproducible run. Never include an API key or personal audio/transcript.

| Date | Path/model | Input | Result | Latency | Usage | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-07-22 | TTS `gpt-4o-mini-tts` | 83-character German fixture | Pass | 1,596 ms | 266,444-byte WAV | Plumbing fixture only; not a voice-quality test. |
| 2026-07-22 | STT `gpt-4o-mini-transcribe` | Same WAV, run 1 | Exact transcript | 927 ms | 79 input / 27 output tokens | Preserved filler, umlauts, private term, and `Späti`. |
| 2026-07-22 | STT `gpt-4o-mini-transcribe` | Same WAV, run 2 | Exact transcript | 888 ms | 79 input / 27 output tokens | Stable result and latency on this synthetic input. |
| 2026-07-22 | Analysis `gpt-5.6-terra`, run 1 | Fixed transcript/schema | Schema-valid | 5,461 ms | 367 input / 661 output tokens | Applied `Keksalarm`; extracted `sagen`; translated all targets. |
| 2026-07-22 | Analysis `gpt-5.6-terra`, run 2 | Fixed transcript/schema | Schema-valid | 8,105 ms | 367 input / 688 output tokens | Lexeme segmentation differed from run 1 despite identical input. |
| 2026-07-22 | Analysis `gpt-5.6-luna` | Fixed transcript/schema | Schema-valid | 4,954 ms | 367 input / 668 output tokens | Fastest sample; natural translations, but surface form for `sagen` was not the literal `sagst`. |
| 2026-07-22 | Analysis `gpt-5.6-terra` | Fixed transcript/schema | Schema-valid | 5,033 ms | 367 input / 644 output tokens | Similar latency to Luna in this single run; Russian preserved a mixed-script private name. |
| 2026-07-22 | Analysis `gpt-5.6-sol` | Fixed transcript/schema | Schema-valid | 8,730 ms | 367 input / 627 output tokens | Slowest sample; good glossary meaning, but omitted the hesitation from natural translations. |
| 2026-07-22 | Capture upload endpoint | 266,444-byte WAV | HTTP 200 | Not separately timed | SHA-256 matched stored upload | Stream-to-partial/move path completed successfully. |
| 2026-07-22 | Capture page, desktop in-app Chromium | Page load | Pass | 161 ms browser event timestamp | WebM/Opus preferred | Secure context, media devices, and service worker available; no console warnings/errors. Microphone permission was intentionally not exercised. |
| 2026-07-22 | Local AnkiIO export | Two corrected snapshots | Round-trip pass | Not benchmarked | Stable GUID and numeric note ID | Package hashes/content differed as intended; actual installed-Anki re-import remains manual. |
| 2026-07-22 | Corrected end-to-end TTS | Same German fixture | Pass | 1,949 ms | 307,244-byte WAV | Measurement includes response body; synthetic speaker omitted the initial filler despite instructions. |
| 2026-07-22 | Corrected end-to-end STT | Corrected-run WAV | Exact to spoken fixture | 742 ms | 88 input / 24 output tokens | Correctly did not invent the filler omitted by TTS. |
| 2026-07-22 | Corrected end-to-end Terra analysis | Corrected-run transcript | Schema-valid | 4,959 ms | 364 input / 729 output tokens | Full response body included; all three calls completed in about 7.65 seconds combined. |

The first batch of API rows measured until response headers were available. The harness now measures through the complete response body; use the corrected end-to-end rows as the baseline and treat the earlier tier comparison as directional until repeated.

## Preliminary findings

- The text analysis call, not transcription or upload, is the current serial bottleneck. On these samples STT took about 0.9 seconds, while analysis took 5.0–8.7 seconds. The immediate phone response should therefore not wait for the complete vocabulary schema.
- A bounded upload/transcription fallback is viable for short clips. The exact synthetic German transcript completed below one second twice, and WebM is supported by the transcription API for real browser captures.
- Synthetic TTS is suitable for repeatable plumbing tests but not for linguistic scoring: a later generated fixture omitted the requested hesitation filler. Quality evals must use fixed, consented human recordings rather than regenerating the audio on every run.
- A strict schema guarantees shape, not linguistic consistency. Identical Terra inputs produced different lexeme boundaries; one Luna item mislabeled the surface form. Deterministic post-processing, protected glossary values, and a labeled eval set remain required.
- The single tier comparison does not justify a final router decision. Luna and Terra were close on latency and broadly usable; Sol was slower without an obvious win on this easy fixture. Repeat across hard real speech before selecting defaults.
- No prompt-cache tokens were reported for these small calls. The fixed schema/prompt should still remain stable, but caching is unlikely to rescue short-request latency by itself.
- Desktop browser plumbing is healthy, but it says nothing conclusive about iPhone launch-time microphone permission. That remains the highest-priority real-device test.
- The local AnkiIO public API is sufficient for a basic multilingual-back-card adapter and preserves the supplied note identity through package round trips. This is necessary but not sufficient evidence that a second import updates instead of duplicates in the target Anki configuration.

## Next experiments

1. Repeat `benchmark-text` five times per tier through a machine-readable runner and calculate p50/p95 instead of copying console output.
2. Test `CaptureWeb` on the exact installed mobile PWA/action-button path, including previously granted permission, fresh permission, locked-screen launch, and poor connectivity.
3. Record five short consented real utterances with casual speech and replay them through the audio harness.
4. Split immediate sentence translation from background lexeme extraction and measure time-to-first-useful-translation.
5. Compare bounded upload against one target-language Realtime session on the same real utterances.
6. Add deterministic lemma/occurrence validation and a fixture scorer before prompt tuning.
7. Add the product-level stable-GUID export/re-import PoC once the local AnkiIO core update settles.
