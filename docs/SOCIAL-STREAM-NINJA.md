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
YouTube  Live/Channel: detected or configured   [ Connect ]   ○ Disconnected
Kick     Channel: gfmaurila   [ Connect ]   ○ Disconnected

ADVANCED
[ ] Use official platform API
```

The future status contract includes provider/platform, connection mode, enabled, connected/state, configured channel or live URL, last-connect time, last-message time and sanitized error category. The current `/api/chat/providers` includes process/transport/capture readiness; `connectionMode` distinguishes `SimpleCapture` from `OfficialApi`. SSN's `platformsObserved` lists platforms for which events have arrived.

## Runtime and isolation

- Dedicated container: `gfm-studioos-socialstream`, image `gfm-studioos/socialstream-ninja:0.4.18`.
- Headless operation uses Xvfb (`DISPLAY=:99`); SSN SSE transport is read by the StudioOS provider.
- SSN has its own persistent `data/runtime/socialstream` volume. The API reads the external config mount as read-only. SSN is not installed inside API, n8n, Ollama, OBS or TruckHub.
- The service has no Docker socket, OBS config mount, Windows browser profile, OBS credentials or host-published port. The image runs as UID 10001, drops Linux capabilities, uses `no-new-privileges`, a read-only root filesystem and bounded tmpfs mounts. It uses no `--privileged` mode.
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

## Response and narration remain future work

Future automation is:

```text
LiveChat -> InteractionDecisionPolicy -> cooldown / anti-spam / anti-loop
  -> Ollama -> IChatResponseSender (platform text)
             -> Piper -> Narration Service -> OBS
```

`IChatResponseSender` and per-platform senders are conceptual boundaries only. Do not connect chat to Ollama, text sending, Piper or OBS narration until a later explicit task. Bot messages must be identifiable and ignored by the automatic decision pipeline to prevent loops.
