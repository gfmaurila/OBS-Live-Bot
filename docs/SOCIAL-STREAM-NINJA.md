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

## Response and narration remain future work

Future automation is:

```text
LiveChat -> InteractionDecisionPolicy -> cooldown / anti-spam / anti-loop
  -> Ollama -> IChatResponseSender (platform text)
             -> Piper -> Narration Service -> OBS
```

`IChatResponseSender` and per-platform senders are conceptual boundaries only. Do not connect chat to Ollama, text sending, Piper or OBS narration until a later explicit task. Bot messages must be identifiable and ignored by the automatic decision pipeline to prevent loops.

## Task09.4 partial validation record — 2026-09-29

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

At the end of this validation, API health is still `degraded` solely because Narration reports `Degraded`. Its queue is empty and it has no playback or failure events. A read-only OBS WebSocket `GetInputList` check confirmed the configured source `GFM StudioOS - Narration` is absent from the current OBS inputs. The health model is behaving as designed: a missing playback source is unavailable even when idle, so this is not an idle-queue false alarm. No audio was started, and Task09.4.1 did not alter OBS inputs or narration runtime configuration. The source's absence was already reported during Task09.4 and is not a regression from this task; recreating it would modify OBS outside this task's authorized scope.
