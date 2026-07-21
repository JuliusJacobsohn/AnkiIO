# AnkiIO Daily Language Deck

## 1. Product summary

AnkiIO Daily Language Deck is a mobile-first .NET web application for capturing words and phrases from everyday German conversation, translating them immediately, and turning new vocabulary into a downloadable personal Anki deck.

The primary scenario is a German-speaking user talking Russian with their wife. When a word or sentence is missing, the user opens the site from the phone's action button, records a short German utterance, and sees a natural translation with minimal delay. The recording is processed in the background into normalized vocabulary in German, English, Russian, and Spanish. Vocabulary already present in that user's deck is not added again.

The existing `AnkiIO` library remains the package-generation boundary. The web product should consume the library rather than add web, identity, persistence, or OpenAI concerns to the library project.

## 2. Goals

- Make recording a missing word or phrase possible within seconds on a phone.
- Show a context-aware, conversational translation as soon as possible after speech.
- Preserve the original audio, transcript, translations, model metadata, and processing results under the authenticated user account.
- Extract useful vocabulary from the complete utterance and normalize each German word to a dictionary form.
- Add only genuinely new vocabulary to the user's deck.
- Generate one multilingual note containing German, English, Russian, and Spanish.
- Let the user download a valid `.apkg` file for later import into Anki.
- Make AI output reviewable and correctable before bad data becomes permanent study material.
- Let users manage their recordings, translations, vocabulary, household glossary, and AI configuration without direct database access.

## 3. Non-goals for the first release

- Writing directly into a user's live Anki collection or syncing through AnkiWeb.
- Replacing Anki scheduling or review functionality.
- Continuous background listening or recording whole conversations.
- Fully automatic speaker identification.
- Guaranteed linguistic perfection without a user correction path.
- Native iOS or Android applications.
- Streaming translated speech back to the user; the first UI result is text.

## 4. Users and language settings

Each user has an authenticated account and owns all recordings, utterances, vocabulary, and deck exports associated with it.

Required language behavior:

- Source language defaults to German but is stored per recording.
- Supported target languages are English, Russian, and Spanish.
- A preferred immediate target language is stored per user; Russian is the initial default.
- The capture screen allows switching the immediate target before or after recording.
- Background processing always produces all three target languages.
- Translations preserve the meaning, tone, and register of casual conversation rather than defaulting to overly formal textbook language.

## 5. Core user journey

1. The user opens the installed site/PWA from a phone shortcut or action button.
2. If microphone permission has already been granted, the capture screen starts recording immediately where the browser permits it. Otherwise it presents one prominent tap-to-record control.
3. The user speaks one short word, phrase, or sentence and stops the recording.
4. The client uploads the audio to the authenticated server. The server persists it before AI processing starts.
5. The UI streams or polls the immediate translation and displays the selected target language first.
6. A background job transcribes the German source, translates the complete utterance into all targets, extracts and normalizes vocabulary, and records structured results.
7. New vocabulary becomes deck entries; duplicates become additional occurrences of an existing entry.
8. The user can review and edit the result, exclude unwanted words, retry failed processing, play the source audio, and download the current deck.

## 6. Functional requirements

### 6.1 Capture experience

- The capture page is responsive, installable as a PWA, and usable one-handed.
- It requests microphone permission only after explaining why it is needed.
- It clearly indicates recording state and elapsed duration.
- It supports stop, cancel, retry, and playback before submission.
- It accepts browser-supported compressed audio such as WebM/Opus and records MIME type, duration, byte length, and checksum.
- Maximum recording duration and upload size are server-enforced and shown to the user.
- Duplicate form submissions are idempotent.
- If the network fails, the client retains an unsent recording locally long enough to retry intentionally.

### 6.2 Immediate translation

- The first result optimizes perceived latency and returns the selected target language.
- The result includes the translated text and a visible processing/error state.
- The translation is natural in context, including colloquial language where appropriate.
- The user can request the other languages without recording again.
- Model IDs are configuration, not hard-coded product behavior.

Initial OpenAI integration decision:

- Evaluate `gpt-realtime-translate` for the immediate path. It is dedicated to streaming speech translation, detects the source language, and returns translated speech plus text; one session has one target language.
- Keep a conventional upload pipeline as the reliable fallback: transcribe the stored audio and send the transcript through the structured text-processing stage.
- Use server-minted ephemeral credentials for any browser-to-Realtime connection. The normal OpenAI API key must never reach the browser.

### 6.3 Background linguistic processing

After the immediate result, one idempotent background workflow must produce a schema-validated result containing:

- the verbatim German transcript;
- a lightly cleaned German transcript without changing meaning;
- sentence-level English, Russian, and Spanish translations;
- detected register and optional translator notes;
- a list of useful lexical items from the utterance;
- the original surface form and character span where available;
- German lemma, part of speech, and morphological details;
- English, Russian, and Spanish lemma translations appropriate to this occurrence;
- confidence/ambiguity flags that can route an item to manual review.

Normalization rules:

- verbs become German infinitives;
- nouns become nominative singular and retain grammatical gender/article;
- adjectives become positive, uninflected dictionary forms;
- pronouns, particles, adverbs, separable prefixes, idioms, and multi-word expressions use an explicit canonical form;
- punctuation, fillers, proper names, and words not useful for study are excluded by default but remain visible in the transcript;
- separable/reflexive verbs and phrases remain intact when splitting them would change the learnable meaning;
- every output is validated against a strict schema before it is stored or applied.

The initial structured-processing model should be configurable. Start evaluation with the current flagship text model (`gpt-5.6-sol` as of 2026-07-21), then benchmark a smaller model against a fixed German/Russian/Spanish evaluation set before choosing the production cost/latency default.

### 6.4 Deduplication

- Deduplication is scoped to a user.
- The first-pass canonical key is normalized German lemma + part of speech + normalized multi-word/reflexive marker.
- Distinct senses may become distinct entries; spelling/case variants and inflected forms must not.
- The database enforces uniqueness for the canonical key so concurrent jobs cannot create duplicates.
- A repeated word creates a new occurrence linked to the original recording and utterance.
- Users can merge incorrectly split entries and separate incorrectly merged senses.

### 6.5 Vocabulary review

- A user can view pending, accepted, excluded, and failed vocabulary.
- A user can correct transcripts, lemmas, translations, grammatical data, examples, and deck inclusion.
- Corrections are retained as user-authored values and are not overwritten by automatic retries.
- Each entry links back to every source occurrence and recording.
- Failed or ambiguous output is recoverable without re-recording.

### 6.6 Recording and translation management

- Users can list, search, filter, sort, paginate, view, edit, trash, restore, and permanently delete their recordings.
- Recording details show audio, capture metadata, transcripts, translations, extracted words, job history, and the deck entries created from the recording.
- Audio bytes are immutable. Replacing a mistaken recording creates a new recording and preserves an explicit relationship to the superseded one.
- Users can create a manual transcript or translation, edit generated text, mark one version as preferred, and restore an earlier revision.
- User-authored corrections are distinguished from model output and take precedence during later processing.
- Reprocessing is an explicit operation with a preview of which generated fields may change. It never overwrites protected user edits.
- Deletion uses a recoverable trash period followed by permanent deletion. Permanent deletion removes the private audio and recording-specific derived content; shared lexemes remain only when supported by another occurrence or explicitly retained as manual vocabulary.
- Bulk delete, bulk reprocess, and bulk accept/exclude require confirmation and report partial failures.
- All mutations use owner authorization, optimistic concurrency, idempotency where appropriate, and a queryable revision/audit history.

### 6.7 Household glossary

Users need a personal database for recurring expressions, nicknames, running gags, private meanings, preferred spellings, and translations that a general-purpose model cannot infer.

- Glossary entries are scoped to an owner. A later household-sharing feature may introduce an explicitly shared scope; entries are private by default.
- An entry stores a source term or phrase, aliases, source language, explanation/context, register, enabled state, priority, and optional validity dates.
- It may store preferred translations and notes independently for German, English, Russian, and Spanish.
- Users can create, view, search, edit, disable, delete, import, and export glossary entries.
- Matching supports exact aliases first and conservative normalized/fuzzy candidates second. The UI shows which glossary entries influenced a result.
- Relevant entries are supplied to immediate translation and background processing as structured reference data, not concatenated as trusted instructions.
- A glossary match guides transcription, translation, lemma selection, and examples but does not bypass schema validation.
- Every model call records the IDs and revision numbers of glossary entries supplied to it so results can be reproduced.
- Editing a glossary entry can optionally enqueue reprocessing of affected recordings after showing the impact and estimated number of calls.

### 6.8 Editable AI prompts and model settings

All application-owned system prompts are stored and versioned in the database. The repository contains seed defaults so a new database is usable and prompt history can be recovered.

- Prompt templates have a stable purpose key, such as `immediate-translation`, `transcription`, `linguistic-processing`, or `deck-entry-generation`.
- Each immutable prompt version stores its system text, required variables, compatible response-schema version, target-language applicability, author, timestamp, notes, and checksum.
- A prompt template has draft, active, and archived versions. Activating or rolling back a version is atomic.
- Users with permission can create drafts, edit them, compare versions, preview rendered prompts using redacted/sample data, validate required placeholders, run an explicit paid test call, activate, archive, and roll back.
- Editing creates a new version; historical versions referenced by model calls never change.
- Seed updates add new defaults but never overwrite an active user-edited version silently.
- Model IDs and permitted model parameters are database-backed settings with validated allowlists and safe defaults. API credentials and other secrets are never stored in prompt records.
- Response schemas remain independently versioned and application-validated. An editable prompt cannot weaken authorization, storage rules, or schema validation.
- Each `ModelCall` references the exact prompt version, schema version, model settings, and glossary revisions used.
- Prompt and model-setting changes are audit logged. Dangerous or invalid configurations fail closed and can be reset to the shipped default.

### 6.9 Anki notes and export

Each vocabulary note contains at least:

- stable application entry ID;
- German lemma and display form;
- part of speech and German gender/article where relevant;
- English translation;
- Russian translation;
- Spanish translation;
- German source sentence;
- all three sentence translations;
- usage/register note;
- source date and tags.

The MVP note type creates a German-front card whose answer shows all three translations and the example sentence. Card templates must be designed so language-specific and reverse cards can be enabled later without changing stored vocabulary.

Export requirements:

- Generate a deterministic per-user `.apkg` snapshot on demand using the existing `AnkiIO` library.
- Reuse stable note GUIDs so importing a later export updates existing notes instead of multiplying them where Anki's import behavior permits.
- Include only accepted entries.
- Display entry count, generation time, and export status.
- Keep export generation idempotent and safe under concurrent requests.
- Record export history and a checksum; old export files may expire independently of vocabulary data.

### 6.10 History and operations

- Users can browse recordings and see every processing stage and failure.
- Jobs retry transient failures with bounded exponential backoff.
- Permanent failures are visible and manually retryable.
- Every external call has a correlation ID, provider request ID when available, model identifier, prompt/schema version, start/end time, status, token/usage data, and error category.
- Administrators can inspect operational metadata without gaining casual access to another user's audio or vocabulary.

## 7. Data model

The relational database stores structured, queryable records rather than treating the provider response as the domain model.

| Entity | Purpose and key fields |
| --- | --- |
| `User` | Identity, locale, preferred immediate target, timestamps |
| `Recording` | Owner, storage key, MIME type, duration, size, SHA-256, capture time, status |
| `Utterance` | Recording, verbatim/clean transcript, source language, user corrections |
| `UtteranceTranslation` | Utterance, target language, text, register, source/model provenance, correction state |
| `ProcessingJob` | Recording, job type, state, attempts, lease, timestamps, last error |
| `ModelCall` | Job, provider request ID, model, prompt/schema version, glossary snapshot, latency, usage, status, retained full response reference |
| `Lexeme` | Owner, German lemma, POS, morphology, canonical key, review state |
| `LexemeTranslation` | Lexeme, target language, translated lemma, notes, provenance/correction state |
| `Occurrence` | Lexeme, utterance, surface form, position, contextual sense |
| `DeckEntry` | Lexeme, stable note GUID, included flag, template version, timestamps |
| `DeckExport` | Owner, object key, checksum, entry count, status, generated/expiry time |
| `GlossaryEntry` | Owner/scope, source term, aliases, context, register, priority, enabled state, revision |
| `GlossaryTranslation` | Glossary entry, language, preferred text, usage note |
| `PromptTemplate` | Stable purpose key, owner/default scope, active version, enabled state |
| `PromptVersion` | Immutable prompt text, variables, compatible schema, author, checksum, lifecycle state |
| `ModelProfile` | Purpose, model ID, validated parameters, owner/default scope, revision |
| `EntityRevision` | Entity type/ID, owner, operation, before/after snapshot or patch, actor, timestamp |

Audio and generated package bytes belong in private object/file storage; the relational database stores their metadata and opaque keys. The complete provider response must also be retained under the user account for audit and reproducibility, with secrets removed and an explicit retention policy. Its important fields are persisted in the normalized rows above, so the raw JSON is evidence rather than the domain model.

All owner-bound queries require an explicit user key. Foreign keys, uniqueness constraints, and concurrency tokens enforce invariants independently of application code.

## 8. Proposed .NET architecture

- ASP.NET Core web application with server-rendered or Blazor-based mobile UI and a small JavaScript microphone interop layer.
- ASP.NET Core Identity with secure persistent login suitable for a personal phone.
- EF Core for relational persistence; SQLite is acceptable for a single-instance MVP, with a planned PostgreSQL migration before multi-instance hosting.
- Private file storage abstraction, initially local disk for development and object storage in production.
- Durable background job abstraction. A database-backed worker is sufficient initially if jobs survive restarts and use leases; introduce a dedicated queue only when deployment requires it.
- Typed OpenAI gateway isolated behind application interfaces, with timeouts, cancellation, retry classification, schema validation, and model configuration.
- Existing `AnkiIO` library for deterministic deck construction and `.apkg` export.
- OpenTelemetry-compatible logs, metrics, and traces with audio/transcript content excluded by default.

During development, the web solution may use a local project reference to `AnkiIO`. The integration boundary must use only its supported public API so the reference can be replaced mechanically with the official `AnkiIO` 1.0.0 NuGet package when available.

Suggested solution boundaries:

```text
src/
  AnkiIO/                 existing reusable Anki package library
  AnkiIO.Daily.Web/       HTTP endpoints, UI, identity, PWA assets
  AnkiIO.Daily.Application/ use cases and ports
  AnkiIO.Daily.Domain/    recordings, utterances, lexemes, deck entries
  AnkiIO.Daily.Infrastructure/ EF Core, storage, OpenAI, jobs, export adapter
tests/
  AnkiIO.Daily.*Tests/
```

## 9. Security, privacy, and data lifecycle

- Keep the OpenAI API key only in server-side environment variables or production secret storage.
- Require HTTPS, secure cookies, CSRF protection, upload validation, rate limiting, and per-user authorization.
- Store audio privately; never expose predictable public URLs.
- Encrypt traffic and use encrypted production storage/backups.
- Do not place raw audio, transcripts, or translations in normal logs.
- Provide account export and deletion, including recordings, derived rows, and generated decks.
- Define configurable retention for audio, raw provider payloads, job logs, and expired deck files.
- Document the selected OpenAI data controls and hosting region before production use.
- Obtain consent from every recorded speaker. The UI must make active recording unmistakable.
- Treat prompt text, transcripts, and model output as untrusted data; encode all rendered content.

## 10. Performance and reliability targets

Initial targets to validate on a real phone and production-like network:

- Capture page interactive within 1.5 seconds on a warm launch.
- Recording starts within 500 ms after the permitted user action.
- Immediate target translation visible within 3 seconds of stopping a typical 3-10 second recording at p50 and within 7 seconds at p95.
- Audio is durably stored before the UI reports successful submission.
- Background vocabulary processing finishes within 30 seconds at p95.
- Deck export of 5,000 entries completes within 30 seconds.
- No accepted recording or user correction is lost across a process restart.

## 11. Acceptance criteria for the MVP

- An authenticated user can record German speech on a supported phone browser.
- The stored recording is linked to only that user and can be played from history.
- A natural Russian translation appears on the capture result screen, with English and Spanish added after processing.
- The structured database contains transcripts, three translations, normalized lexemes, occurrences, model provenance, and job state.
- `Ich bin nach Hause gegangen` can produce the verb lemma `gehen`, not a separate entry for `gegangen`.
- Repeating the same normalized word in another recording does not create a duplicate deck entry.
- A user correction survives a retry or reprocessing operation.
- A user can trash and restore a recording, edit a translation with revision history, and permanently delete the recording and its private derived data.
- A configured running gag is translated using its household meaning, and the result identifies the glossary revision that influenced it.
- A user can draft, validate, activate, and roll back a system-prompt version; subsequent model calls reference the selected immutable version.
- The user can download an `.apkg`, import it into the supported Anki version, and see a German-front note with all three translations.
- API keys and another user's data cannot be obtained from browser traffic or predictable URLs.
- Automated tests cover authorization, idempotency, normalization fixtures, deduplication races, job retries, and package export.

## 12. Risks and decisions to validate early

- iOS/browser rules may prevent true auto-recording without a user gesture even when launched from an action button. Test an installed PWA on the exact phone before designing around zero taps.
- `gpt-realtime-translate` produces one target language per session. The immediate path should use the preferred language; the background pass produces all languages without tripling live sessions.
- General transcription can lose the model's richer interpretation of speech. Store both the immediate translation provenance and the authoritative background transcript instead of assuming they are identical.
- Lemma-only deduplication incorrectly merges homonyms. Include part of speech now and add sense separation through review/evaluation.
- `.apkg` update semantics and note GUID behavior must be verified against the supported Anki release using an isolated profile.
- Casual Russian quality, aspect pairs, stress marks, grammatical gender, and profanity/register require a human-reviewed evaluation set.

## 13. Source notes

Architecture choices were checked against current OpenAI documentation on 2026-07-21:

- [Realtime and audio](https://developers.openai.com/api/docs/guides/realtime)
- [Speech to text](https://developers.openai.com/api/docs/guides/speech-to-text)
- [Structured model outputs](https://developers.openai.com/api/docs/guides/structured-outputs)
- [Realtime translation guide](https://developers.openai.com/cookbook/examples/voice_solutions/realtime_translation_guide)
- [Data controls](https://developers.openai.com/api/docs/guides/your-data)

Model aliases and capabilities change. Re-run the model evaluation and update configuration before implementation or production rollout rather than treating this document's model names as permanent.
