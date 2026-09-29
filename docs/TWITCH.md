# Twitch Chat Integration

## Task status

OBS-LIVE-BOT-09 simple capture is complete: Task09.3.1 validated a real Twitch message through Social Stream Ninja `classic` capture and the StudioOS ingestion pipeline. The generic Windows credential helper, API helper client, Device Code endpoints, token validation/refresh code, and EventSub provider are implemented as an optional advanced mode. OAuth/EventSub has not been validated against the user's Twitch account and must not be reported as authenticated or connected.

## Credential boundary

`ObsLiveBot.Api` remains in Linux Docker. Windows DPAPI `CurrentUser` is available only to a process running as the signed-in Windows user, so the generic **GFM StudioOS Secure Credential Helper** runs on the Windows host and persists opaque provider/key records protected with DPAPI. Task 09 initially uses the generic record `Twitch/OAuthTokens`; the helper is not Twitch-specific and exposes no credential enumeration API.

The current Docker Desktop topology does not provide a Windows named pipe to the Linux API container. The local bridge uses a minimal HTTP endpoint on the host gateway, a shared IPC bearer key configured locally in both processes, a Docker-peer IP allowlist, bounded payloads, strict provider/key names, no Swagger, and no credential diagnostics. The helper binds on host interfaces, so HTTP is not intrinsically LAN-private: access depends on its peer allowlist, bearer authentication, and host firewall. Read-only discovery observed the API at `172.21.0.2` on Docker network `172.21.0.0/16`; the helper's current default allowlist (`192.168.65.0/24`) does not match and therefore fails closed on this machine. No helper listener or host firewall rule was enabled/tested. Before starting it, configure the exact API peer (ideally a stable `/32`) and validate denied access from a LAN peer; do not broaden the allowlist merely to make connectivity work. The IPC key is not a Twitch credential and must not be committed.

At rest, Twitch access/refresh tokens belong only in the helper's DPAPI-protected store. They may exist transiently in API process memory for Twitch protocol calls, but must not enter container files, `.env`, logs, images, n8n, or Git. Helper unavailability leaves Twitch authentication unavailable and must not take down the API, OBS, chat, Ollama, Piper, or n8n.

## OAuth and EventSub target

The approved flow is Twitch Device Code Flow for a public installed client, without a client secret. The intended minimum read scope is `user:read:chat`; sending chat messages is out of scope. Device authorization, polling, token validation/refresh, revocation, OAuth state handling, and secure token lifecycle are not yet runtime-validated.

The chat transport implementation uses EventSub WebSocket with `channel.chat.message` v1 only. It waits for `session_welcome`, resolves the configured broadcaster through Helix, subscribes using broadcaster and authenticated user IDs, validates tokens on connection and hourly, refreshes before expiry, recognizes keepalive/reconnect/revocation frames, maps full text/fragments, and sends normalized events through the existing validation/dedupe/MediatR/LiveChatBuffer pipeline. Twitch identity remains provider-scoped. Ordinary chat does not itself guarantee an interaction; the existing explicit command/decision policy and OBS narration autoplay remain separate.

The `session_reconnect` implementation now opens the supplied reconnect URL while retaining the current socket, validates the replacement `session_welcome`, promotes the replacement, and only then retires the old socket. It does not issue a new subscription during migration because Twitch documents that the reconnect URL transfers existing subscriptions. If replacement setup fails, the candidate socket is retired and the provider resumes receiving on the old socket for recovery until it closes or its keepalive expires. This handoff has not yet been validated against Twitch or a deterministic socket-level test double. Event revocation deletes protected credentials; the authorization diagnostic snapshot may remain stale until the next refresh. WebSocket protocol, Helix subscription, retries, token refresh, revocation, and the handoff still require automated protocol tests and live account validation before this provider is complete.

## Local non-secret configuration

Configure `GFM_STUDIOOS_CONFIG_PATH` in the ignored `.env` to the host folder containing `studioos.providers.json`; Compose mounts it read-only at `/app/config`. Twitch's non-secret `enabled`, `clientId`, `channel`, and optional `broadcasterUserId` values belong under `providers.twitch`. The current external file is `D:\OBS-Live\.config\studioos.providers.json`; runtime validation recognizes Twitch as enabled and configured for channel `gfmaurila`, with broadcaster ID deferred. This is configuration only: authorization remains required and no OAuth/EventSub connection was performed in this checkpoint. YouTube is enabled with a public Client ID and deferred channel ID. Kick integration remains pending; its current external configuration was observed enabled during Task 09.1 and deliberately left untouched, although this checkpoint expects it disabled. Configure the same random helper IPC key (at least 32 UTF-8 bytes) in the Windows helper process and `GFM_STUDIOOS_CREDENTIAL_HELPER_SHARED_KEY` in `.env`. Do not provide or configure a Twitch client secret for this read-only Device Flow. Never place access tokens, refresh tokens, authorization codes, or the helper key in versioned files or provider JSON.

The provider JSON is loaded at API startup (`reloadOnChange=false`); restart only the `api` Compose service after edits. Missing or invalid configuration is handled fail-closed: the API remains available, all provider integrations stay disabled, and logs report a sanitized reason code without file contents or secret values.

## Official references

- [Twitch chat authentication](https://dev.twitch.tv/docs/chat/authenticating/)
- [Twitch EventSub WebSocket handling](https://dev.twitch.tv/docs/eventsub/handling-websocket-events)
- [Twitch EventSub WebSocket reference](https://dev.twitch.tv/docs/eventsub/websocket-reference)
- [Twitch EventSub subscription types](https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/)
- [Twitch OAuth getting tokens, including Device Code Flow](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth)
- [Twitch token refresh](https://dev.twitch.tv/docs/authentication/refresh-tokens/)
- [Twitch token validation](https://dev.twitch.tv/docs/authentication/validate-tokens/)
