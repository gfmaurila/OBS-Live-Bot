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
