# Implementation plan

This plan converts `PRODUCT_REQUIREMENTS.md` into small, testable milestones. Check a milestone only when its exit criteria are met; avoid building the full AI workflow before the phone capture and Anki export risks have been proven.

## Phase 0 - Validate the two highest-risk assumptions

- [ ] Record the exact target phone, OS, browser, and intended action-button shortcut.
- [ ] Build a disposable HTTPS microphone page and test whether an installed PWA can begin recording on launch after permission has been granted.
- [ ] Document the minimum unavoidable tap on iOS/Android if autoplay policy blocks zero-tap capture.
- [ ] Make a small authenticated server-side OpenAI smoke test using the existing environment key; do not print the key or send it to a browser.
- [ ] Send a short consented German audio fixture through `gpt-realtime-translate` with Russian as the target and record latency and returned text events.
- [ ] Verify the non-realtime fallback with stored audio, transcription, and a translated response.
- [ ] Create a tiny deck with the existing `AnkiIO` library, import it into an isolated supported Anki profile, export a changed version with the same note GUID, and verify update behavior.
- [ ] Write ADRs for capture transport, immediate translation path, database choice, file storage, and background-job mechanism.

Exit criteria: the exact phone workflow is known, one end-to-end API call works, and stable-note re-import behavior is demonstrated.

## Phase 1 - Establish the product solution

- [ ] Add `AnkiIO.Daily.Domain`, `AnkiIO.Daily.Application`, `AnkiIO.Daily.Infrastructure`, and `AnkiIO.Daily.Web` projects without changing the public responsibilities of `AnkiIO`.
- [ ] Add focused unit, integration, architecture, and browser-test projects.
- [ ] Add configuration options for model IDs, duration/size limits, storage paths, retention, and job timing.
- [ ] Add local development secrets guidance and safe configuration validation at startup.
- [ ] Add a development database and migrations.
- [ ] Add CI jobs for restore, build, tests, formatting/analyzers, and migration validation.
- [ ] Add a local development runbook and sample configuration containing no credentials.

Exit criteria: a clean checkout builds, tests, migrates the database, and starts the web application.

## Phase 2 - Identity and private storage

- [ ] Add ASP.NET Core Identity with login, logout, secure cookie settings, and one initial account path.
- [ ] Model user ownership explicitly on every private aggregate.
- [ ] Implement authorization tests proving users cannot enumerate, fetch, update, or delete each other's resources.
- [ ] Define `Recording` metadata and processing-state transitions.
- [ ] Implement a private file-storage interface and local development implementation.
- [ ] Validate upload MIME type, extension-independent content signature where practical, size, duration, and checksum.
- [ ] Persist recording metadata and bytes atomically enough that partial uploads are recoverable/cleanable.
- [ ] Add authenticated playback with range support and non-public URLs.
- [ ] Add account/data deletion primitives and retention-job skeleton.

Exit criteria: two test users can upload and play only their own audio, and failed uploads leave no successful database record.

## Phase 3 - Mobile capture MVP

- [ ] Create an installable PWA shell and manifest.
- [ ] Build the one-handed capture screen with clear idle, permission, recording, uploading, processing, success, and error states.
- [ ] Add the minimal JavaScript interop around `MediaRecorder` and negotiate supported MIME types.
- [ ] Cache the preferred immediate language and allow rapid switching among Russian, English, and Spanish.
- [ ] Add stop, cancel, playback, retry, upload progress, and safe local retry behavior.
- [ ] Add a client-generated idempotency key per recording.
- [ ] Cap recording duration on both client and server.
- [ ] Run browser tests plus manual tests on the exact target phone, including locked-screen/action-button launch and poor connectivity.

Exit criteria: a real phone can reliably submit a short recording with the fewest browser-permitted actions.

## Phase 4 - Immediate translation

- [ ] Implement an OpenAI gateway with server-only credentials, cancellation, timeouts, correlation IDs, and error classification.
- [ ] Implement short-lived ephemeral credential issuance if the chosen Realtime transport connects from the browser.
- [ ] Implement the preferred-language `gpt-realtime-translate` path and capture translated text events.
- [ ] Implement the stored-audio fallback path.
- [ ] Persist the recording before declaring the submission successful.
- [ ] Persist immediate translation, target language, model ID, provider request/session ID, latency, and status as normalized records.
- [ ] Stream status/result to the page with a reconnect-safe mechanism.
- [ ] Add rate limits and per-user concurrency limits.
- [ ] Add fixtures for casual speech, code-switching, background noise, silence, and provider errors.
- [ ] Measure p50/p95 phone-to-result latency and decide whether Realtime complexity earns its place over upload + transcription.

Exit criteria: the selected translation appears reliably and meets the agreed latency target, with a tested fallback and no key in browser-visible configuration.

## Phase 5 - Durable background workflow

- [ ] Create `ProcessingJob` and `ModelCall` tables with state-machine constraints, attempt counts, leases, and timestamps.
- [ ] Implement a hosted worker that survives restarts and cannot process one job concurrently without a lease.
- [ ] Add bounded retry/backoff for transient failures and a terminal/manual-retry state.
- [ ] Version prompts and response schemas independently of model configuration.
- [ ] Store usage, latency, provider IDs, and redacted raw payloads according to retention policy.
- [ ] Add an operational history view without logging transcript/audio content.
- [ ] Test crash recovery at every state transition.

Exit criteria: killing the application during processing causes safe continuation or retry, never loss or duplicate application of results.

## Phase 6 - Structured transcription, translation, and morphology

- [ ] Define the strict structured-output schema described in the requirements.
- [ ] Build a curated, consented evaluation set covering casual German, compound nouns, separable/reflexive verbs, idioms, fillers, Russian aspect/stress/register, and ambiguous senses.
- [ ] Implement transcription/cleaning and the single background structured model call.
- [ ] Validate schema, language codes, allowed parts of speech, spans, and non-empty fields before persistence.
- [ ] Persist `Utterance`, `UtteranceTranslation`, lexeme candidates, translations, morphology, and model provenance in normalized rows.
- [ ] Route low-confidence/ambiguous candidates to review instead of silently accepting them.
- [ ] Compare `gpt-5.6-sol` with suitable smaller current models on accuracy, latency, and cost; record the decision rather than relying on model branding.
- [ ] Add regression tests for every corrected model mistake.

Exit criteria: the fixed evaluation set reaches an agreed accuracy threshold and all accepted output is schema-valid and queryable without parsing provider JSON.

## Phase 7 - Deduplication and review

- [ ] Implement the canonical-key algorithm and document Unicode/case/whitespace normalization.
- [ ] Add a per-user unique database constraint for the canonical key.
- [ ] Upsert lexemes and occurrences transactionally under concurrent jobs.
- [ ] Preserve distinct senses through explicit user separation while merging inflected variants.
- [ ] Build recording history and result-detail pages.
- [ ] Build vocabulary queues for pending, accepted, excluded, ambiguous, and failed items.
- [ ] Add transcript, lemma, morphology, translation, sentence, and register editing.
- [ ] Mark corrected fields as user-owned so reprocessing cannot overwrite them.
- [ ] Add merge, split, exclude, include, reprocess, and retry actions with audit history.
- [ ] Test races by processing the same word in simultaneous recordings.

Exit criteria: repeats create occurrences, not duplicate entries; users can repair every relevant class of AI mistake.

## Phase 8 - Anki note generation and download

- [ ] Define and version the multilingual note type, CSS, and stable field order.
- [ ] Define a stable GUID derived from the application deck-entry identity, not translated text.
- [ ] Implement German-front/all-languages-back cards through the existing `AnkiIO` API.
- [ ] Add source sentences, translations, grammar, register, dates, and tags with safe HTML encoding.
- [ ] Generate deterministic per-user deck snapshots from accepted entries only.
- [ ] Persist `DeckExport` state, count, checksum, storage key, and expiry.
- [ ] Serialize concurrent export requests per user or reuse an equivalent completed snapshot.
- [ ] Add authenticated download with a safe filename.
- [ ] Verify first import, repeated import, corrected-note update, Unicode, large decks, and corrupt/failure cases in an isolated Anki profile.
- [ ] Add optional language-specific/reverse card templates only after the MVP template is stable.

Exit criteria: the user downloads and repeatedly imports a valid deck without duplicate notes, and corrections appear after a later export/import.

## Phase 9 - Privacy, observability, and production readiness

- [ ] Complete a threat model for authentication, IDOR, CSRF, uploads, stored XSS, prompt injection, rate abuse, secrets, and backups.
- [ ] Confirm HTTPS, secure headers/cookies, anti-forgery, output encoding, and per-endpoint authorization.
- [ ] Choose production database and private object storage; test backup and restore.
- [ ] Implement audio, raw-payload, job-log, and expired-export retention policies.
- [ ] Implement full account export/deletion and verify deletion from primary storage and backup lifecycle.
- [ ] Document recording consent and privacy terms suitable for the intended household use.
- [ ] Configure OpenAI project limits, spend alerts, allowed models, data controls, and region after reviewing current account options.
- [ ] Add redacted structured logs, metrics, traces, health checks, and alerts for error rate, queue age, latency, and spend.
- [ ] Load-test uploads, background jobs, and 5,000-entry exports.
- [ ] Add deployment, rollback, secret-rotation, incident, and recovery runbooks.
- [ ] Run a final mobile accessibility and poor-network pass.

Exit criteria: production deployment is reproducible, monitored, recoverable, private by default, and has an explicit operating budget.

## Phase 10 - Post-MVP options

- [ ] Add spoken translated output where it improves the conversation flow.
- [ ] Add opt-in reverse and target-specific cards.
- [ ] Add deck filters by target, date, review state, or household member.
- [ ] Add user-controlled audio on Anki cards after consent, file-size, and privacy review.
- [ ] Add direct AnkiConnect or AnkiWeb integration only after separately assessing security and API/support constraints.
- [ ] Evaluate native share-sheet/action-button wrappers only if browser launch friction remains unacceptable.

## Definition of done for every phase

- [ ] Acceptance behavior is covered by automated tests at the appropriate level.
- [ ] Security and owner isolation are tested for every new private resource.
- [ ] Database changes have forward migrations and a tested rollback/recovery story.
- [ ] Errors are observable without exposing secrets, audio, or transcript content.
- [ ] User-facing failure states include a recovery action.
- [ ] Documentation and configuration examples match the shipped behavior.
- [ ] Relevant tests pass from a clean checkout.
