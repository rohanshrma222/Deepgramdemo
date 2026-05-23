# MarineAI Unity Demo Architecture

## Purpose

This project is a Unity 2022.3 voice assistant demo for marine-biology conversations. It captures a user voice prompt, transcribes it with Deepgram STT, sends the transcript to Gemini, streams Gemini's answer back into the UI, and speaks the response with Deepgram TTS.

## Runtime Context

- Runtime: Unity 2022.3.0f1
- Scene: `Assets/Scenes/MarineAI_Demo.unity`
- Main runtime object: a scene GameObject with `MarineAgentManager`, `DeepgramSTT`, `GeminiAPI`, `DeepgramTTS`, `AudioSource`, and `UIManager`
- UI: Built at runtime by `UIManager` using UGUI and TextMesh Pro
- Configuration asset: `Assets/Resources/ApiConfig.asset`
- Secrets: loaded from environment variables or local `.env`

## High-Level Component Diagram

```text
User
  |
  | clicks Talk / selects EN or DE
  v
UIManager
  |
  | StartPipeline(), SetLanguage()
  v
MarineAgentManager
  |
  | RecordAndTranscribe()
  v
DeepgramSTT  ---- websocket audio ---->  Deepgram Listen API
  |                                      wss://api.deepgram.com/v1/listen
  | transcript
  v
MarineAgentManager
  |
  | SendChatMessage()
  v
GeminiAPI  ---- streaming SSE ---->  Gemini generateContent stream API
  |
  | response deltas
  v
MarineAgentManager
  |
  | sentence-sized chunks
  v
DeepgramTTS  ---- HTTPS request ---->  Deepgram Speak API
  |
  | AudioClip
  v
AudioSource
```

## Main Runtime Flow

1. `UIManager` builds the interface and binds button events to `MarineAgentManager`.
2. The user selects `EN` or `DE`.
3. `MarineAgentManager.SetLanguage()` applies the language across STT, Gemini, and TTS.
4. The user clicks `Talk`.
5. `MarineAgentManager.RunPipeline()` moves state to `Listening`.
6. `DeepgramSTT.RecordAndTranscribe()` validates the Deepgram key, opens a websocket transcription session, starts microphone capture, converts audio to 16-bit PCM, and sends audio frames.
7. Deepgram returns final transcript results. `MarineAgentManager` updates `LastTranscript` and the UI.
8. `MarineAgentManager` moves state to `Thinking` and starts a TTS worker coroutine.
9. `GeminiAPI.SendChatMessage()` calls Gemini streaming generation and emits response deltas as they arrive.
10. `MarineAgentManager.HandleGeminiResponseDelta()` updates the visible response text and queues sentence-sized chunks for TTS.
11. `DeepgramTTS.SpeakText()` synthesizes queued chunks and plays them through the shared `AudioSource`.
12. When Gemini and queued TTS complete, `MarineAgentManager` returns to `Idle`.

## Core Components

### MarineAgentManager

File: `Assets/Scripts/MarineAgentManager.cs`

Responsibilities:

- Owns the voice-agent state machine: `Idle`, `Listening`, `Thinking`, `Speaking`
- Coordinates STT, Gemini, TTS, UI, and `AudioSource`
- Applies language changes to all dependent services
- Starts and guards the end-to-end pipeline
- Maintains latest transcript and response text
- Splits streamed Gemini deltas into chunks for TTS

Key design points:

- `StartPipeline()` ignores duplicate runs while a pipeline is already active.
- `RunPipeline()` is implemented as a Unity coroutine so it can coordinate Unity APIs and async service calls.
- Gemini response deltas are buffered until a sentence or soft boundary is found, then queued for TTS.

### DeepgramSTT

File: `Assets/Scripts/DeepgramSTT.cs`

Responsibilities:

- Loads Deepgram config from `ApiConfig`
- Validates the Deepgram key with `/v1/auth/token`
- Requests microphone authorization
- Opens a `ClientWebSocket` to Deepgram Listen
- Captures microphone audio at the device's native sample rate
- Resamples and downmixes audio to `linear16`, 16 kHz, mono
- Sends PCM frames to Deepgram
- Receives final transcript messages

Deepgram request shape:

```text
wss://api.deepgram.com/v1/listen
  ?model=nova-3-general
  &language=en|de
  &encoding=linear16
  &sample_rate=16000
  &channels=1
  &smart_format=true
  &punctuate=true
  &utterances=true
  &interim_results=true
  &endpointing=1800
  &utterance_end_ms=2200
  &vad_events=true
  &keyterm=...
```

Accuracy-oriented behavior:

- The websocket task is started before microphone audio is consumed.
- The capture cursor starts from sample `0`, avoiding lost first words.
- Marine-domain keyterms bias Nova-3 toward relevant command phrases and terms.

### GeminiAPI

File: `Assets/Scripts/GeminiAPI.cs`

Responsibilities:

- Maintains Gemini conversation history
- Builds Gemini request body with system instruction and language instruction
- Calls Gemini streaming generation via `streamGenerateContent?alt=sse`
- Parses Server-Sent Event chunks using `GeminiStreamDownloadHandler`
- Emits `OnResponseDeltaReceived` for live UI and TTS chunking
- Emits `OnResponseReceived` after the final full answer

Language behavior:

- `EN` tells Gemini to answer in English.
- `DE` tells Gemini to answer in German.
- Conversation history is cleared when language changes.

### DeepgramTTS

File: `Assets/Scripts/DeepgramTTS.cs`

Responsibilities:

- Converts text chunks to speech using Deepgram Speak
- Selects the active Deepgram voice model from `ApiConfig`
- Downloads WAV audio through `DownloadHandlerAudioClip`
- Falls back to manual PCM extraction if Unity cannot decode the clip directly
- Plays each synthesized clip through `AudioSource`

Current tradeoff:

- TTS chunks are synthesized sequentially. This lowers time-to-first-audio, but can introduce a pause between chunks because each chunk requires a separate HTTPS synthesis request.

### UIManager

File: `Assets/Scripts/UIManager.cs`

Responsibilities:

- Creates the runtime UI canvas, panels, buttons, and text areas
- Binds `Talk`, `Retry`, `EN`, and `DE` buttons
- Reflects current agent state in the UI
- Displays transcript, response text, and errors
- Tracks selected language button state

### ApiConfig

File: `Assets/Scripts/ApiConfig.cs`

Responsibilities:

- Stores model and language configuration
- Loads `DEEPGRAM_API_KEY` and `GEMINI_API_KEY`
- Reads keys from environment variables first, then `.env`, then serialized fallback fields
- Normalizes language codes to `en` or `de`
- Applies language-specific TTS model selection

Important behavior:

- Environment loading is static and cached. Restart Unity after editing `.env` to guarantee fresh key loading.
- The serialized API key fallbacks should stay empty in source control.

### Audio Utilities

Files:

- `Assets/Scripts/AudioPcmUtility.cs`
- `Assets/Scripts/WavUtility.cs`

Responsibilities:

- Convert Unity float samples to PCM16
- Downmix multi-channel input to mono
- Resample microphone input to the STT target sample rate
- Convert PCM16 bytes back to Unity float samples for TTS fallback playback
- Parse WAV data when needed

## Language Design

The UI language selection controls all language-sensitive services:

```text
EN selected
  -> Deepgram STT language=en
  -> Gemini answers in English
  -> Deepgram TTS model=aura-asteria-en

DE selected
  -> Deepgram STT language=de
  -> Gemini answers in German
  -> Deepgram TTS model=aura-2-lara-de
```

The current default in `ApiConfig.asset` is English.

## Data Flow Details

### Audio Input Path

```text
Microphone
  -> Unity AudioClip samples
  -> AudioPcmUtility downmix/resample
  -> PCM16 16 kHz mono byte chunks
  -> Deepgram websocket binary frames
  -> Deepgram Results JSON
  -> final transcript string
```

### Gemini Streaming Path

```text
Transcript
  -> Gemini request JSON
  -> SSE data events
  -> GeminiStreamDownloadHandler
  -> response delta strings
  -> UI text append
  -> TTS sentence chunk buffer
```

### TTS Output Path

```text
Response chunk
  -> Deepgram Speak HTTPS POST
  -> WAV response
  -> Unity AudioClip
  -> AudioSource playback
```

## Error Handling

Major user-facing errors are surfaced through `MarineAgentManager.ReportError()` and `UIManager.ShowGeminiError()`.

Current error categories:

- Missing `ApiConfig`
- Missing or rejected Deepgram key
- Missing microphone
- Denied microphone permission
- Deepgram websocket failure or timeout
- Gemini API request failure
- Gemini stream parse failure
- Empty Gemini response
- Deepgram TTS request failure
- Missing `AudioSource`

## External Dependencies

Unity packages:

- `com.unity.nuget.newtonsoft-json`
- `com.unity.modules.audio`
- `com.unity.modules.unitywebrequest`
- `com.unity.modules.unitywebrequestaudio`
- `com.unity.textmeshpro`
- `com.unity.ugui`

External services:

- Deepgram Listen API for STT
- Deepgram Speak API for TTS
- Google Gemini API for response generation

## Security Model

API keys are expected to be local development secrets.

Recommended local `.env`:

```text
DEEPGRAM_API_KEY=...
GEMINI_API_KEY=...
```

The `.env` file is gitignored. Do not commit keys into `ApiConfig.asset`.

For production, do not ship provider API keys inside a Unity client. Use a backend token broker or proxy service that mints short-lived credentials and enforces usage limits.

## Current Limitations

- TTS chunk playback is sequential, so pauses may occur between chunks.
- STT accuracy depends heavily on microphone quality, room noise, language selection, and user timing.
- API keys are read from local environment or `.env`; this is suitable for demos but not production client distribution.
- `ClientWebSocket` behavior can vary across Unity platforms. The current implementation is intended for desktop/editor use.
- UI is built programmatically at runtime, so visual layout is controlled in code rather than a Unity Canvas prefab.

## Recommended Next Improvements

1. Prefetch TTS clips while the current clip is playing to reduce pauses between spoken chunks.
2. Add optional debug logging for Deepgram alternatives and confidence scores.
3. Add a visible language indicator near the Talk button so users know which language STT expects.
4. Move API access behind a small backend service for production use.
5. Replace programmatic UI construction with prefabs if the visual design will grow.
