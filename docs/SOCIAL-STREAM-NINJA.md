# Social Stream Ninja — simple chat capture

## Product boundary

Social Stream Ninja (SSN) is an optional Infrastructure capture engine behind `ILiveChatProvider`. It is not a domain dependency. Twitch, YouTube and Kick capture events follow the same path:

```text
Platform chat -> dedicated SSN Docker/headless service -> SSE
  -> SocialStreamNinjaLiveChatProvider -> normalization/validation/deduplication
  -> MediatR -> LiveChatBuffer -> read-only chat APIs
```

The selected engine can be replaced by another provider without changing LiveChat, Interaction, Ollama, Piper, narration or the Command Center. Identity remains provider-scoped (`Provider + ProviderUserId`). Unknown event types are retained as `Unknown` with sanitized metadata.

## Simple capture and official APIs

Simple capture is the default. Its settings belong in the external `studioos.socialstream.json` and are limited to engine enablement, SSN endpoint, source setup options and public channel/live inputs. Do not put OAuth tokens, client secrets, stream keys, browser cookies or session files copied from another browser profile there.

`studioos.providers.json` remains available for optional authenticated platform actions. Its existing public settings and Client IDs are preserved. `officialApiEnabled` is a separate opt-in; absent/false leaves official adapters off even when legacy `enabled` and `clientId` values are present. Official API actions may later use their own `[ Connect ]` flow. This task does not implement platform message sending.

Google Cloud / YouTube Data API is not required for simple YouTube chat if the selected SSN capture mode can obtain that live chat without it. The same rule applies to Twitch and Kick developer-console apps. SSN capabilities report Twitch/Kick username or URL input and YouTube video ID or URL input. This documents engine support; it does not claim that each platform's real chat was validated in this run.

## Intended user experience

```text
CHAT CONNECTIONS

Twitch   Channel: gfmaurila   [ Connect ]   ● Connected
YouTube  Channel: gfmaurila   [ Connect ]   ● Connected
Kick     Channel: gfmaurila   [ Connect ]   ○ Disconnected

ADVANCED
[ ] Use official platform API
```

The future status contract includes provider/platform, connection mode, enabled, connected/state, configured channel or live URL, last-connect time, last-message time and sanitized error category. The current `/api/chat/providers` includes process/transport/capture readiness; `connectionMode` distinguishes `SimpleCapture` from `OfficialApi`. SSN's `platformsObserved` lists platforms for which events have arrived.

## Runtime and isolation

- Dedicated container: `gfm-studioos-socialstream`, image `gfm-studioos/socialstream-ninja:0.4.18`.
- Headless operation uses Xvfb (`DISPLAY=:99`); SSN SSE transport is read by the StudioOS provider.
- SSN has its own persistent `data/runtime/socialstream` mount. The API reads the external config mount as read-only. SSN is not installed inside API, n8n, Ollama, OBS or TruckHub.
- The service has no Docker socket, OBS config mount, Windows browser profile, OBS credentials or broad host credential-store mount. Only loopback callback relays on host ports 8080/8181 are published for the hosted OAuth callback. The image runs as UID 10001, drops Linux capabilities, uses `no-new-privileges`, a read-only root filesystem and bounded tmpfs mounts. It uses no `--privileged` mode.
- API and SSN share only the internal capture network; SSN also uses a separate egress network for provider connectivity. n8n, Ollama, OBS and TruckHub do not depend on SSN availability.
- `SSAPP_CONTROL_API` is bound to loopback in-container and relayed to the API-facing Docker network by `socat`; only the API and SSN participate in that internal network.

## Task09.3 validation record — 2026-09-29

Task09.3.1 is complete for Twitch simple capture. YouTube and Kick real-chat validation remain outside this result.

| Check | Observation |
|---|---|
| SSN version/source | Pinned release image `0.4.18`, AppImage SHA-256 verified during image build definition |
| Container | `gfm-studioos-socialstream`; dedicated service, no host port |
| Startup/headless | Container started; Xvfb and SSApp startup entries present; Docker health became `healthy` |
| Transport | SSN capabilities endpoint reports ready; provider reports SSE, process/transport/capture ready |
| Recovery | Initial stopped-container recovery reached Connected in about 27 seconds. Final restart and isolated recreation preserved the API process and returned the provider to Connected/capture-ready within the first ~2-second poll |
| API/chat aggregate | After deploying the tested API change, `/api/chat/providers` reports SSN `SimpleCapture` Connected and official Twitch/YouTube Disabled by default; `/api/chat/state` is Ready |
| Synthetic post-reconnect | PASS: SSN-compatible ASCII fixture appeared in messages/events through SocialStreamNinjaProvider, normalization and MediatR; no API restart |
| Twitch simple source | SSN's default WebSocket mode reported `Please sign in`. The supported `classic` mode was tested: source became `active` with no auth error. The API adapter now selects `classic` for Twitch simple capture |
| Kick simple source | Source `gfmaurila` reports active, but emitted capture count is 0 and no chat event reached the buffer; real chat remains unvalidated |
| YouTube simple source | Enabled in SSN configuration, but no channel/video URL source exists; no active live URL was identified |
| Real Twitch | PASS: a real Twitch message arrived over the SSN SSE stream, was normalized as `Twitch/Message`, passed MediatR and appeared in the bounded buffer. Validation inspected only presence and sanitized metadata, not message text |
| AI/TTS | PASS: both captured messages were ignored with `ExternalCaptureAutoResponseDisabled`; completed AI interactions, TTS and narrations remained zero; `AutoPlayInteractions=false` |
| Persistence | PASS: dedicated bind mount confirmed; Twitch `classic` source stayed `active` with no error after both container restart and isolated force-recreation. API container/process and the two buffered messages were preserved |
| Regression/build | PASS: 237/237 tests; full solution build with 0 errors and 0 warnings; Compose config and diff checks passed |
| Secret scan | PASS: exposed OBS credential absent from all versionable files; high-confidence token/private-key patterns absent; `.env` and `data/runtime` remain ignored |
| Resources | One `docker stats` sample: 346.4 MiB memory (2.18% of 15.53 GiB), 2.60% CPU, 200 PIDs |

The SSN-owned Electron user-data volume contains Chromium cookie/session stores. Their contents were not read or copied from Windows; no manual login was required for the validated Twitch `classic` capture. The persistence test preserved the dedicated bind mount without inspecting browser data.

The API logs initially recorded `OBS_AUTH_FAILED` from `ObsConnectionManager` at roughly 30-second intervals, and OBS logs showed corresponding localhost WebSocket client attempts. SSN has no OBS password/config mount and does not integrate with OBS WebSocket. Diagnostic validation on 2026-09-29 proved the root cause: **the OBS WebSocket credential had changed while StudioOS still held/expected the previous credential**. Restoring the previous OBS credential, without rotating it or changing StudioOS, returned `/api/obs/status` to `Connected` and `/api/obs/live-state` to `synchronized=true`; the repeated authentication notification stopped after `OBS_CONNECTED`.

The credential used during troubleshooting was exposed. After Task09.3.1 diagnostic validation is complete, a mandatory security follow-up must rotate the OBS WebSocket credential and update OBS plus StudioOS secure credential storage together. The new value must never be printed, logged, documented, committed or copied into provider JSON files.

The active API process also reported the Twitch Credential Helper unavailable (`shared_key_not_configured`). This affects official authenticated Twitch/EventSub mode only. It is not a requirement for simple SSN reading and must remain opt-in.

## Response and narration boundary

Future automation is:

```text
LiveChat -> InteractionDecisionPolicy -> cooldown / anti-spam / anti-loop
  -> Ollama -> IChatResponseSender (platform text)
             -> Piper -> Narration Service -> OBS
```

`IChatResponseSender` and per-platform senders are conceptual boundaries only; written replies remain unimplemented. Task10 connects eligible chat to Ollama/Piper/OBS audio only. Bot/self messages are excluded from the automatic decision pipeline to prevent loops. The controlled test completed and `AutoPlayInteractions` was returned to false.

## Task09.4 partial validation record — 2026-09-29

## Task10 — YouTube live_chat hardening and real audio E2E

SSN `0.4.18` watch-page/classic source did not reliably expose the YouTube live-chat DOM in the validated StudioOS headless environment. A separate source using YouTube's supported `/live_chat?is_popout=1&v=<videoId>` route captured real messages and completed the real Task10 audio path. This finding is scoped to the tested environment; it is not a claim of a universal SSN defect.

For the first validated live, `youtube-url-1t69en` was the active canonical source and used `/live_chat`; the former watch source `youtube-url-ldu71d` was stopped but retained (not deleted) for reversible rollback. SSN `youtubeAutoAdd` is false to avoid watch-page auto-add competing with the canonical route. Twitch and Kick sources were not changed. No OAuth/session, safe storage or persistent SSN profile data was changed. Runtime source reconciliation uses SSN's supported source API and a deterministic per-video idempotency key; it only stops same-video duplicates when SSN exposes an exact matching video ID. Unidentifiable source records are not stopped blindly.

StudioOS must send the explicit `videoId` alongside the popout `url` when it calls SSN `addSource`. Without it SSN cannot derive its stable per-live identity (`<target>-vid-<videoId>`) and falls back to a hash of the normalized URL path, which is identical for every `/live_chat` popout URL; the stored record then has an empty `videoId`, so later same-video duplicate detection can never match and successive lives accumulate suffixed duplicates such as `youtube-url-<id>-dup-1`. Sending `videoId` makes the SSN record self-describing, keeps same-live reconciliation idempotent through the per-video idempotency key, and prevents a previous live's source from being reused. The per-video idempotency key is derived from the video ID, so a different live can never replay a stale source.

StudioOS accepts an optional public YouTube `liveChatUrl` in the external social-stream config. Infrastructure validates HTTPS, the official YouTube host/path, the popout flag and an 11-character video ID; Domain does not know YouTube DOM or URL rules. SSN remains responsible for browser/DOM capture. The current public config points at the live used in validation; it is per-live metadata, not a credential.

Automatic discovery of the authenticated channel's current live was not reliably demonstrated in SSN 0.4.18. Therefore it is **not implemented/claimed**: each new live requires supplying its public live_chat URL through the supported configuration/source-management boundary. No custom scraping is used. Real YouTube input and Task10 AI/audio E2E were proven; written platform replies remain out of scope and `AutoPlayInteractions` is false after the controlled test.

The active external public configuration was backed up with Windows DPAPI CurrentUser protection and then migrated without copying the historical credential values. YouTube now uses public `channel` plus `authMode: oauth`; Twitch and Kick public channels were preserved. The loader/validator rejects forbidden credential names case-insensitively and leaves the API healthy with a sanitized failure reason. Full tests passed 258/258 and the solution built with zero errors and zero warnings.

SSN 0.4.18 exposed the expected account-based YouTube option and an independent URL/video-ID fallback. Selecting the account flow failed before Google authorization with `SSAPP_YOUTUBE_OWNER_SECURE_STORAGE_UNAVAILABLE`: secure token storage was not available in the Linux container. There was no existing YouTube session or selected channel, and no cookie/token content was inspected. A plaintext password-store workaround was intentionally not enabled. Consequently automatic live discovery, OAuth persistence, restart validation and real YouTube chat remain unvalidated.

The existing Twitch `classic` source remained active and the Kick `gfmaurila` source remained active in observed WebSocket mode. Kick simple reading did not require a StudioOS official API or public webhook in this topology, but no real Kick event was observed during Task09.4. Provider identity and cross-provider dedupe remain covered by unit tests; they do not substitute for the required real YouTube/Kick messages. Task09.4 remains PARTIAL and no automatic AI, text, TTS or narration was enabled.

Final regression checks for Task09.4 found OBS Connected and synchronized, SSN Connected/healthy, Swagger available, Ollama/Piper Ready and n8n healthy. Overall API health remained `degraded` because the narration subsystem reported `Degraded`; no narration started or completed. Task09.4 did not alter OBS configuration to repair this condition.

## Task09.4.1 validation record — 2026-09-30

The SSN 0.4.18 owner-account package was inspected before changing the runtime. It uses Electron `safeStorage.isEncryptionAvailable()` and encrypts owner access/refresh token values with `safeStorage.encryptString`; plaintext fallback is explicitly refused. On Linux, Electron selects `gnome_libsecret` with `--password-store=gnome-libsecret`. The backend is libsecret calling Secret Service over the SSN session D-Bus, implemented here by GNOME Keyring. The previous root cause was startup order: the entrypoint called `gnome-keyring-daemon --start` before initializing/unlocking the daemon, so the Secret Service control socket was unavailable. The corrected entrypoint initializes/unlocks first, starts the secrets component, then verifies the Secret Service owner.

| Check | Observation |
|---|---|
| SSN version | 0.4.18; exact packaged `app.asar` inspected |
| Safe storage | Electron `safeStorage`; Linux backend `gnome_libsecret`/libsecret/Secret Service with GNOME Keyring |
| D-Bus / keyring / libsecret | Required: yes / yes / yes |
| Desktop session / headless | Desktop session not required; headless works with session D-Bus plus Xvfb |
| Plaintext storage | Disabled. Optional OAuth-token seeding into YouTube WebSocket `localStorage` is disabled in the packaged app; validated chat uses classic public live sources |
| Persistence | `/var/lib/socialstream` maps to ignored `data/runtime/socialstream`; the keyring unlock file is an external read-only Compose secret. No runtime/keyring/session files are versioned or exposed by an API |
| Google flow | SSN hosted authorization opened Google's official page. Analyst entered credentials and approved the channel there. No StudioOS password form, Google Cloud project or client secret was used |
| Public channel | `gfmaurila`; channel ID `UCjy19AugQHIhyE0Nv558jcQ` |
| Live discovery | SSN owner discovery was requested, but did not create a live source. The active public live was identified from the channel and added through SSN's supported URL source API as the allowed fallback |
| Real YouTube chat | PASS: SSN raw `youtube` message events for the controlled text included the active video ID and native message ID; StudioOS recorded YouTube via SSE, normalized it, published through MediatR, and returned it from the chat buffer API |
| Real Kick chat | PASS: SSN raw `kick` message event was captured and appeared as Kick in the StudioOS buffer through the same provider pipeline |
| Identity and dedupe | Provider identity remains `Provider + ProviderUserId`. YouTube messages lacked a native author ID, so the mapper used a stable provider-scoped hash of the author name; this is reported as `identity.synthetic=true` and does not mark the source event as synthetic. The mapper reads SSN's nested `meta.messageId` for dedupe |
| Restart and recreation | PASS: only SSN was restarted, then only SSN was recreated with its persistent bind intact. `safeStorage` owner store still identified the channel and both encrypted token records remained present; YouTube and Kick sources were reactivated without another Google login. StudioOS API was not restarted |
| Automation | `AutoPlayInteractions=false`; YouTube/Kick interactions were ignored as `ExternalCaptureAutoResponseDisabled`; AI completions, text replies, TTS and narration starts remained zero |

The public configuration contains only public provider/channel/auth-mode settings. StudioOS never receives or stores the Google password. Token, cookie and keyring contents were not printed or exposed.

## OBS-LIVE-BOT-10.1.2 — automatic YouTube live discovery

Automatic discovery is now implemented and owns the per-live YouTube source. The previous "supply a URL per live" workaround is no longer required for normal operation.

### How the current live is discovered

Discovery reads the channel's own public streams page (`https://www.youtube.com/@<handle>/streams`) and takes the entry that carries YouTube's `LIVE` badge, whose animation target is that entry's video ID. A finished live, a playlist and a scheduled premiere all use different badge styles, so only a genuinely live entry is reported.

This is channel-scoped public information. It needs no Google Cloud project, no OAuth, no API key, no stream key, and it never reads a search results page. Only a public video ID leaves the discovery component, and that ID is already part of the public live URL. The configured value is validated as a plain channel handle or a `UC…` channel ID before any URL is built, so a configured channel cannot become an arbitrary URL or a different host.

SSN's own `youtubeAutoAdd` stays **off** in this mode. Its discovery was previously unreliable and produced sources StudioOS could not reconcile, so two owners must never race on the same live.

### Ownership and lifecycle

```text
OBS stream state / periodic tick
  -> YouTubeLiveOrchestrator (idempotent reconciler, current OBS state is the truth)
  -> IYouTubeLiveDiscovery (public streams page)
  -> IYouTubeChatSourceManager (canonical youtube-vid-<videoId> via SSN source API)
```

- The canonical identity is `youtube-vid-<videoId>`. The same live always resolves to the same source, so a repeated transition never creates a duplicate.
- The SSN chat provider no longer creates or starts a YouTube source while discovery owns it. It still manages Twitch and Kick exactly as before, and an explicit manual `liveChatUrl` retains the previous provider-managed behavior.
- A finished live is stopped, never deleted, so the record stays auditable and reversible. Ownership is always cleared when the live ends, so a previous live can never be reused.
- The reconciler wakes on a live-state signal **and** on a periodic tick. OBS can stay streaming across a YouTube live change, so a steady state is still re-checked. Every entry point converges to the same result: false→true discovers, true→true does not duplicate, true→false releases, and StudioOS restart, OBS reconnect and SSN restart all reconcile without recreating a working source.
- If the page briefly exposes more than one live badge, the live already validated wins while it is still live, which avoids flapping between two simultaneous lives.
- Discovery and SSN failures are contained: they never affect the OBS connection, chat ingestion, Twitch or Kick.

### Configuration

`liveDiscovery` is an optional public block in `studioos.socialstream.json`. An explicit `manualLiveChatUrl` always takes precedence and disables automatic discovery.

| Field | Default | Meaning |
|---|---|---|
| `enabled` | `true` when the block exists | Enables automatic discovery |
| `channel` | — | Public handle (`gfmaurila`/`@gfmaurila`) or `UC…` channel ID |
| `manualLiveChatUrl` | — | Per-live override; disables discovery while set |
| `autoReleaseOnEnd` | `true` | Stop the source when the live ends, preserving the record |
| `minDiscoveryIntervalSeconds` | `30` | Minimum spacing between discovery attempts and the worker tick period |
| `requestTimeoutSeconds` | `20` | Bounded per-request timeout |
| `maxResponseBytes` | `4194304` | Hard cap on the page read into memory |

`GET /api/youtube/live-discovery` is a read-only projection of ownership: enabled state, current public video ID, its public live and chat URLs, the SSN source ID, the last discovery method and reason, and the observed active source list. It never starts, stops or reconfigures anything, and it returns no secret.

### Validation record — 2026-10-01

Validated against a real live broadcast, with OBS streaming and recording left running throughout.

| Check | Observation |
|---|---|
| Discovery | PASS: the live was found automatically with no URL supplied, method `ChannelStreamsPage` |
| Source identity | PASS: `_j0cCIamgpc` mapped to the existing canonical `youtube-vid-_j0cCIamgpc`; outcome `Reused`, `retiredDuplicates=0` |
| No duplication | PASS: exactly one active YouTube source; active set was `twitch-user-gfmaurila`, `kick-user-gfmaurila`, `youtube-vid-_j0cCIamgpc` |
| SSN untouched | PASS: the SSN container was never restarted or recreated during the run, so the source was reused rather than re-added |
| Periodic tick | PASS: discovery attempts advanced on the tick while OBS state stayed steady, with no source churn |
| Real chat on the discovered source | PASS: a real YouTube message arrived over SSE on `youtube-vid-_j0cCIamgpc`, was normalized, published through MediatR and returned by the chat API |
| AI/TTS boundary | PASS: that message was decided `Ignore`; `responseText`, AI provider/model, TTS provider and `audioPath` were all null, and narration reported `started=0`, `completed=0`, `lastPlaybackAtUtc=null` |
| `AutoPlayInteractions` | PASS: still `false`; trigger `!studio` unchanged |
| OBS safety | PASS: `Connected`, scene `Iniciando`, streaming and recording never stopped or restarted; no scene, source, audio or Ulanzi change |
| Regression/build | PASS: 335/335 tests; API build with 0 errors and 0 warnings |

Two defects were found and fixed during this task rather than left in place. `HttpClient.MaxResponseContentBufferSize = 0` is rejected by .NET and crash-looped the container on first deploy. A manual `liveChatUrl` override reported a "manual override" reason while still running discovery; it now genuinely disables discovery. A third gap was that the heartbeat logic existed but nothing invoked it while OBS state stayed steady, so the worker now wakes on a tick as well as on signals.

Known limitation: YouTube's public page structure is an internal detail. The live-badge pattern is therefore pinned by unit tests, but a future YouTube change could require updating `YouTubeStreamsPageRules`. The failure mode is safe: no live badge is found, no source is changed, and `lastReason` reports it.

At the end of this validation, API health is still `degraded` solely because Narration reports `Degraded`. Its queue is empty and it has no playback or failure events. A read-only OBS WebSocket `GetInputList` check confirmed the configured source `GFM StudioOS - Narration` is absent from the current OBS inputs. The health model is behaving as designed: a missing playback source is unavailable even when idle, so this is not an idle-queue false alarm. No audio was started, and Task09.4.1 did not alter OBS inputs or narration runtime configuration. The source's absence was already reported during Task09.4 and is not a regression from this task; recreating it would modify OBS outside this task's authorized scope.
