# OBS Narration — OBS-LIVE-BOT-08

## Implemented

Narration is a separate Application subsystem (`INarrationService`) that accepts only a validated Piper artifact and controls the dedicated OBS source through `IAudioPlaybackService`. The adapter uses `IObsRequestClient`, which is the existing OBS WebSocket client/connection; it does not create another OBS socket. Interaction orchestration does not call OBS directly.

The selected mechanism is OBS's built-in Media Source (`ffmpeg_source`). A dedicated source named `GFM StudioOS - Narration` is shared by scene items in the existing scenes. It is not reused for MIC, GAME, DISCORD, MUSIC, ALERTS, or BROWSER. On explicit playback, the adapter sets the audio file, restarts the media input, observes `GetMediaInputStatus` until `Playing`, and waits for `Ended`; start and playback timeouts are safety limits. On completion/failure it stops and clears the file so a later API startup cannot replay the prior audio.

No virtual audio driver/plugin was needed. This keeps routing local and independently controllable while avoiding new host dependencies.

## OBS routing discovered

- OBS 32.1.2; WebSocket 5.7.3; collection and profile `ETS`; active scene at discovery `Iniciando`.
- Existing collection had six scenes and 30 inputs before the change. No narration source existed. The built-in `ffmpeg_source` kind is supported.
- In the observed profile, `TrackIndex=1` and the recording configuration uses `RecTracks=1`; narration is therefore routed only to track 1. Tracks 2–6 are disabled for the narration source.
- Monitoring is `MonitorOff` (`OBS_MONITORING_TYPE_NONE`). This avoids a second local monitor route, feedback, and duplicate monitoring. The output remains available to the configured stream/recording track.
- Smoke validation did not start a public stream or local recording. No changes were made to encoder/video/profile, OBS audio devices, or other source volume/mute/track settings.

## Queue and safety

| Setting | Default/behavior |
|---|---|
| Enabled | `true` |
| AutoPlayInteractions | `false` |
| Source | `GFM StudioOS - Narration` |
| Queue | bounded FIFO, maximum 5 |
| Concurrent playback | 1; overlap prohibited |
| Interruption | do not interrupt current narration |
| Maximum media duration | 15 seconds |
| Start timeout | 5 seconds |
| Playback timeout | 25 seconds, additionally bounded against media duration |
| Default volume | 70%, independently applied to Narration |
| Monitoring | `MonitorOff` |
| Runtime artifact path | container `/app/data/runtime/tts`; host path configured by `Narration:HostRuntimeDirectory` |

Only WAV artifacts produced and registered by the TTS pipeline are accepted. The container path must remain under `Interactions:Tts:OutputDirectory` / `Narration:AllowedRuntimeDirectory` and use the interaction GUID filename. The container validates the actual mounted artifact, then maps its basename to the configured Windows host runtime directory for OBS. No API accepts a filesystem path. Piper cleanup skips artifacts leased while queued or playing.

Playback, validation, path mapping, OBS disconnect, full queue, duration, and timeout failures are reported explicitly. An item that fails is not replayed after OBS reconnect. Text/AI/TTS results remain independent from the narration playback result. Mute and volume are source-local; APIs cannot mute OBS globally or change another input.

## API

- `GET /api/narration/state` — readiness, current item, queue length/capacity, source, volume/mute, monitoring/tracks, counts and recent playback timestamps.
- `GET /api/narration/recent` — bounded narration results and events.
- `PUT /api/narration/mute` — JSON `{ "muted": true }` or `false`; affects Narration only.
- `PUT /api/narration/volume` — JSON `{ "volume": 70 }`; safe range 0–100.
- `POST /api/narration/dev/test` — Development environment only. JSON `{ "text": "Teste de narração do GFM StudioOS." }`; calls the selected real Piper provider and then enqueues the artifact. It never accepts a path and uses the production application queue/playback path.

Health exposes a separate `narration` status. A missing source, disconnected OBS, or routing mismatch degrades Narration without making OBS Chat/Ollama/Piper/n8n themselves fail. Normal startup never creates a source or plays historical interaction-buffer content. Explicit narration playback calls the idempotent `EnsureSourceAsync`: it reuses a correctly typed source, creates the owned source only when absent, repairs missing scene attachments, and fails with `NARRATION_SOURCE_NAME_COLLISION` for an incompatible same-name input. If an owned source already exists, the worker stops/clears stale media without replay.

## Validation record

The controlled real smoke test used Piper and OBS Media Source while streaming and recording were stopped. The TTS artifact was RIFF/WAVE PCM, 22,050 Hz, mono, 16-bit, 115,360 bytes, 2.615 seconds. Runtime state sequence was `Queued → Preparing → Playing → Completed`; `GetMediaInputStatus` supplied the Playing/Ended evidence. The item was visible in the bounded recent buffer. Mute/unmute and volume controls were exercised and restored to unmuted/70%.

`REAL OBS NARRATION: YES` means the local OBS Media Source completed playback on its configured output track. It does not mean audio was monitored through speakers or heard in a public stream. `OBS AUDIO PLAYBACK` in the sense of local monitoring remains `NO` (`MonitorOff`). No public stream or local recording was started.

## Development versus future

- Implemented: explicit API-driven narration playback, real Piper WAV, bounded FIFO, path/artifact safety, OBS controls/health/state.
- Development: the smoke endpoint is available only when ASP.NET Core environment is `Development`.
- Future: automatic chat-to-voice remains disabled until `Narration:AutoPlayInteractions=true` is deliberately approved/configured. UI, n8n publisher, per-platform mixing, local monitor playback, and public live automation are not implemented by this document.

The Piper voice `pt_BR-faber-medium` redistribution status remains `UNKNOWN`; this task does not change its licensing determination. See [TTS.md](TTS.md).

## Task09.5 recovery record — 2026-09-29 (local)

OBS 32.1.2 / WebSocket 5.7.3 was connected to scene collection/profile `ETS`, program scene `Iniciando`; streaming and recording were stopped before recovery. Current config and its `.bak` had no narration source references, while historical OBS logs and Task08 documentation show that the source had existed previously. No deletion or collection-replacement event was recorded, so the root cause is **UNKNOWN**.

Before changing OBS, a timestamped safety package was written outside the repository at `D:\OBS-Live\.config\backups\obs-narration\obs-narration-20260929-222826.dpapi`. It contains `global.ini`, `basic/profiles/ETS/basic.ini` and `basic/scenes/ETS.json`, a UTC manifest with per-file SHA-256/length, and rollback instructions. The ZIP payload is protected with Windows DPAPI CurrentUser, the directory ACL is restricted to the current Windows user, and the backup was decrypted to a temporary validation area and all three hashes verified. Rollback requires OBS closed and restoration only of the listed files. The backup is not in Git.

The one controlled Development smoke invoked the existing `EnsureSourceAsync` through the narration test endpoint. OBS now has exactly one `GFM StudioOS - Narration` input of kind `ffmpeg_source`, attached once to each of the six existing `ETS` scenes (`Iniciando`, `Jogando - ETS`, `Já Volto`, `Finalizando`, `Jogando - ATS`, `Jogo`). No scenes or unrelated sources were recreated or modified. Routing is Track 1 only and `MonitorOff`. Mute→unmute and volume 35%→70% were validated through source-local APIs; the final source is unmuted at 70%.

Piper (`pt_BR-faber-medium`) produced a real 2.59-second WAV for “Teste de narração do GFM StudioOS.” The one narration moved through `Queued → Preparing → Started → Completed` (OBS Media Source reported playback start and end). Narration health is `Ready`; the API recent buffer has one completed manual test with no failures. No public stream or recording was started. `AutoPlayInteractions=false`; prior chat events remain input-only and produced zero automatic AI, TTS or narration. Task09.4.1 remains PASS and its Twitch/YouTube/Kick validation and secure YouTube session were preserved.

## Dual voice narration — OBS-LIVE-BOT-10.2

### Implemented

One interaction now produces two narrations instead of one. The roles are semantic and are never inferred from a voice filename or from a perceived gender:

| Role | Purpose | Voice | Order |
|---|---|---|---|
| `Chat` | Deterministic repeat of the accepted viewer message, trigger removed | `pt_BR-jeff-medium` | 1 |
| `Assistant` | The generated reply from the local model | `pt_BR-faber-medium` | 2 |

The chat clip is a pure function of the chat event: it never consults the model. It is built and its synthesis started **before** the AI call, so the viewer's message is heard without waiting for the reply. The reply synthesis starts as soon as the sanitized text exists, and the group is handed to the narrator with that synthesis still in flight, so playback overlaps synthesis instead of waiting for it.

The spoken chat text is deterministic: the trigger is removed using the same rule the trigger check uses, the optional `"{username} disse: {message}"` prefix is applied, URLs are replaced with `"link"`, whitespace and control characters are normalized, punctuation is collapsed, the body is bounded in length, and empty speech is rejected.

### Why a coordinator exists

The two clips of one interaction are produced at different times and interactions overlap, so a plain FIFO would allow `A chat, B chat, A assistant`. `DualVoiceNarrationCoordinator` assigns each interaction a monotonically increasing group sequence and admits groups in that order, which yields `A chat, A assistant, B chat, B assistant`.

Ordering key: a group is ordered by the moment its reply text became available. A slow reply is therefore spoken after a faster reply that arrived later, but its own chat clip is never delayed by it.

The coordinator adds ordering, not a second audio system. The narration queue is still the single bounded FIFO with a single reader, and playback is still strictly serial, so two clips can never sound at the same time. Admission is bounded by `MaxPendingGroups` (8); a newcomer beyond that limit is rejected with `COORDINATOR_SATURATED` rather than buffered.

### Failure isolation

| Failure | Result |
|---|---|
| Chat synthesis fails or throws | Assistant reply still speaks |
| AI fails, reply rejected, or reply cannot be voiced | Chat clip still speaks |
| Narrator rejects an item or throws | Interaction still completes; the group does not stall the groups behind it |
| Narrator never returns | Interaction is not blocked; narration is downstream of the reply |

A role reaches the queue only when its audio actually exists. A failed, canceled, or missing role is skipped rather than propagated.

### Configuration

| Setting | Default | Notes |
|---|---|---|
| `Narration:Enabled` | `true` | |
| `Narration:AutoPlayInteractions` | `false` | Must stay `false` until audible playback is authorized |
| `Narration:ChatVoice:Enabled` | `true` | Disabling it must never silence the assistant |
| `Narration:ChatVoice:VoiceId` | `pt_BR-jeff-medium` | |
| `Narration:ChatVoice:Volume` | `100` | Logical, applied immediately before that role plays |
| `Narration:ChatVoice:SpeakUserName` | `true` | |
| `Narration:ChatVoice:UserNameFormat` | `{username} disse: {message}` | Only `{username}` and `{message}` are valid tokens |
| `Narration:ChatVoice:MaxMessageCharacters` | `240` | |
| `Narration:AssistantVoice:Enabled` | `true` | |
| `Narration:AssistantVoice:VoiceId` | `pt_BR-faber-medium` | Empty falls back to the pre-existing `Interactions:Tts:Voice` |
| `Narration:AssistantVoice:Volume` | `90` | |
| `Interactions:Tts:VoicesDirectory` | `/opt/tts-engine/voices` | Where per-role models and their `.onnx.json` configs are resolved |

Role volume is logical: it is applied to the item immediately before that role plays and is never adopted as the narration-wide volume, so the roles can be balanced independently without feedback between the OBS source volume and the item volume.

Piper derives a voice's config file as the model path plus `.json`, so the real config file of a voice is `<voice>.onnx.json`. The catalog refuses voice ids containing path separators, `.` or other unsafe characters, and requires both the model and its config to exist under `VoicesDirectory`.

### API

`GET /api/narration/state` now includes a `voices` array in playback order with `role`, `enabled`, `voiceId`, `volume`, and the chat `userNameFormat`, so the configured roles can be verified without reading the configuration file or the model filenames. `GET /api/narration/recent` items now carry `voiceRole`, `orderWithinInteraction`, and `groupSequence`, which is what makes the two-voice ordering verifiable after a test.

### OBS impact

None. The same single `GFM StudioOS - Narration` source, `TrackIndex=1`, and `MonitorOff` are used, and this task changed no OBS setting, scene, or input. Two voices are two queue items played strictly one after the other, never two sources.

### Validation status of this task

Implemented and covered by automated tests: 414 unit tests pass and the Release build reports 0 errors and 0 warnings. **Audible dual voice playback has NOT been performed.** This phase deliberately kept `Narration:AutoPlayInteractions=false`, produced no audible output, and changed no OBS configuration. See [TTS.md](TTS.md) for the two voice models and their licensing status.
