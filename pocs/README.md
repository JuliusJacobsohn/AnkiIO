# Daily Language Deck proof-of-concepts

These projects deliberately live outside `AnkiIO.sln`. They test product risks without coupling experimental web or OpenAI code to the actively developed AnkiIO library.

## Audio pipeline

`AudioPipeline` measures the bounded-recording path:

1. optionally synthesize a short German fixture;
2. upload it for German transcription;
3. submit the transcript plus a household glossary to a strict structured-output call;
4. print request IDs, token usage, latency, and schema-validated output.

The normal OpenAI key is read only from `OPENAI_API_KEY`. It is never printed or written. Defaults can be changed with:

```powershell
$env:ANKIIO_TRANSCRIBE_MODEL = 'gpt-4o-mini-transcribe'
$env:ANKIIO_ANALYSIS_MODEL = 'gpt-5.6-terra'
$env:ANKIIO_TTS_MODEL = 'gpt-4o-mini-tts'
```

Run a complete synthetic smoke test:

```powershell
dotnet run --project pocs/AudioPipeline -- full artifacts/poc-audio
```

Run only structured analysis:

```powershell
dotnet run --project pocs/AudioPipeline -- analyze "Äh, wenn du wieder Keksalarm sagst, gehen wir nachher noch kurz zum Späti, oder?"
```

Compare the configured GPT-5.6 tiers on the same text:

```powershell
dotnet run --project pocs/AudioPipeline -- benchmark-text
```

Transcribe and analyze a real browser recording:

```powershell
dotnet run --project pocs/AudioPipeline -- audio path/to/recording.webm
```

All example text is synthetic. Do not commit personal recordings or raw provider responses.

## Capture web

`CaptureWeb` is an intentionally unauthenticated local experiment for browser/PWA behavior. It records timing events and uploads the resulting file to local disk. Do not bind it to a public interface or use sensitive speech.

Trust the local ASP.NET development certificate if necessary, then run:

```powershell
dotnet dev-certs https --trust
dotnet run --project pocs/CaptureWeb
```

Open the printed HTTPS URL on the target phone over a trusted development setup. Enable **Try auto-start on the next launch**, grant microphone permission, close the installed PWA, and launch it again from the intended action-button shortcut. Copy the event log into the research notes.

The server writes captures below `artifacts/capture-poc`, which is ignored by Git.

## Anki export identity

`AnkiExport` references the local AnkiIO project only through its public API. It creates two package snapshots for the same vocabulary entry: the second contains a corrected Russian translation but reuses the note GUID and numeric note ID. It then reads both packages back and verifies that identity is stable and content changed.

```powershell
dotnet run --project pocs/AnkiExport -- artifacts/poc-decks
```

Import `daily-language-v1.apkg` and then `daily-language-v2.apkg` into an isolated Anki profile to test the installed Anki version's update/import settings. The PoC never opens or modifies an Anki profile itself.
