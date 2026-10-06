# CHAT RESPONSES (OBS-LIVE-BOT-11)

## What this capability is

StudioOS can write a reply as text into a live chat channel, behind a single boundary,
`IChatResponseSender`. It is a capability of its own, separate from reading chat and separate from
speaking: a viewer may hear the reply, read it, or both.

It ships **disabled**. `ChatResponses:Enabled` is `false` in the shipped configuration and an operator
turns it on deliberately, then turns it back off deliberately. Nothing about the capability survives a
restart in an enabled state, because the only runtime-mutable setting is held in memory.

## Why writing text is its own risk

Reading a chat has no effect on the chat. Writing into one does: the reply comes back through the exact
same capture path as a viewer's message. Two failure modes follow from that and both are handled here.

1. **Loop.** A written reply returns as a new chat message. If StudioOS treated it as a viewer message it
   would answer itself, indefinitely. See [Loop guard](#loop-guard-identity-first-text-only-as-bootstrap).
2. **Duplication.** A retried, replayed or concurrently admitted write would post the same line twice.
   See [Idempotency](#idempotency).

Everything else in this document exists to keep one of those two from ever reaching an audience.

## Flow

```text
InteractionCompletedNotification (Respond + Completed + text)
  -> WrittenChatResponseHandler        never throws, never delays the interaction
  -> ChatResponseGatekeeper            every gate, ordered, returns a verdict instead of throwing
  -> ChatResponseWriter                bounded queue, single reader, off the interaction path
  -> IChatResponseSender               the only part that talks to a platform
  -> IChatResponseLedger.RecordWrite   the text becomes "ours" only after the platform accepted it
```

The reader is single on purpose. Two concurrent writes into one chat channel are exactly the duplication
this capability exists to avoid. The queue is bounded so a stalled platform cannot turn the writer into an
unbounded memory leak, and when it is full the **newest** reply is the one refused, so a burst never
silently discards work that was already accepted.

## Gates

`ChatResponseGatekeeper.Admit` applies every condition in order, cheapest and most fundamental first, so a
rejection always names the first real reason:

| Order | Reason code | Condition |
|---|---|---|
| 1 | `WRITTEN_RESPONSES_DISABLED` | The master switch is off. |
| 2 | `CHAT_SENDER_NOT_SELECTED` | `ChatResponses:Sender` names no registered sender. |
| 3 | `DEVELOPMENT_SENDER_NOT_ALLOWED` | A simulated sender is selected while `AllowDevelopmentSender` is false. |
| 4 | `CHAT_SENDER_UNAVAILABLE` | The selected sender cannot write right now. |
| 5 | `PROVIDER_NOT_SUPPORTED` | The sender has no transport for this platform. |
| 6 | `PROVIDER_NOT_ALLOWED` | `AllowedProviders` excludes this platform. |
| 7 | `STUDIOOS_OWN_MESSAGE` | The account is one StudioOS posts from. |
| 8 | `EMPTY_TEXT` / platform text error | Normalization collapsed the text to nothing. |
| 9 | `DUPLICATE_TEXT` | The same text was already written on this channel inside the echo window. |
| 10 | `GLOBAL_COOLDOWN` / `USER_COOLDOWN` | Rate limiting. |
| 11 | `DUPLICATE_WRITE` | This exact idempotency key was already claimed. |

The idempotency key is claimed **last**, after every other gate passed, so a reply refused for a transient
reason keeps its identity available for a later explicit retry.

## Loop guard: identity first, text only as bootstrap

Identity is the guard. Text is only the bootstrap.

A written reply returns as an ordinary chat message. Matching its text would be a weak guard: a viewer can
quote it verbatim, a platform can reflow or re-case it, and truncation makes two genuinely different replies
collide. So the account that produced the reply is what StudioOS recognises.

- `ChatResponses:SelfActorIdentities` lists accounts StudioOS posts from, in `Provider:Account` form. An
  account identity is the same account everywhere on that platform, so the channel is deliberately not part
  of the comparison.
- The **first** time an echo of a written reply is captured, the account that delivered it is *learned*.
  From then on that account is recognised by identity alone and its text is never compared again.
- When either matches, the captured event is tagged with `interaction.generatedByStudioOS=true`, which the
  interaction decision policy already treats as `SelfMessage`. No new interaction, and therefore no new
  reply, can start from it.
- The written-reply gate refuses `STUDIOOS_OWN_MESSAGE` as well. The capability therefore cannot answer
  itself even if the capture-side tagging were ever removed.

The guard lives in `LiveChatIngestionPipeline` because that is the only place on the capture side that sees
both the message and its account. It runs after deduplication and before publication, so the buffer, the
decision and the writer never have to know it exists. A guard that throws is caught and the message is
published untagged: a broken loop guard must never be the reason a viewer message fails to appear.

`Duplicate text inside the echo window` is a second, independent suppression: the same text on the same
channel inside `EchoWindowSeconds` is refused, which also stops a canned answer being posted twice. Text is
collapsed before comparison, because a reply that differs from a previous one only by spacing looks
identical to a viewer while defeating text-based detection.

## Idempotency

The key is `provider|channel|<chatResponseId>`, and `chatResponseId` **is** the interaction id when the
write came from an interaction. Replaying the same completed interaction — by a retry, a duplicated
notification or a manual request — therefore resolves to the same key.

Claiming the key is the suppression mechanism; there is no separate "committed" set. A key is claimed
before the write is attempted and released again when the attempt did not start or did not succeed, so a
transient platform error does not permanently block a reply and a host shutdown does not lose one.

## Failure isolation

The callers of this capability are interactions, OBS, audio, narration and capture. None of them may be
taken down by a problem with writing text, so nothing here throws to them:

| Failure | Recorded as |
|---|---|
| Queue full | `Skipped` / `CHAT_QUEUE_FULL`, key released |
| Host stopping | `Cancelled` / `CHAT_WRITE_CANCELLED`, key released |
| No sender selected | `Failed` / `CHAT_SENDER_NOT_SELECTED` |
| Per-attempt ceiling exceeded | `Failed` / `CHAT_WRITE_TIMEOUT` |
| Sender threw | `Failed` / `CHAT_SEND_EXCEPTION` |
| Transport rejected or unreachable | `Failed` / the SSN error code |
| Notification or event publication failed | logged; the write and its result are unaffected |

Nothing is persisted. A restart forgets every key, every cooldown and every learned identity and starts
clean, which is the correct behaviour: an in-memory ledger cannot carry a stale suppression into a new
stream.

## Senders

| Sender | Delivers? | Notes |
|---|---|---|
| `SocialStreamNinja` | Yes | The real write path. |
| `Development` | No | Deterministic; reports success and `Simulated=true`. Never a fallback. |

`Development` exists so the whole pipeline can be exercised on a machine with no live chat at all. It is
selected only by an explicit name, and `ChatResponses:AllowDevelopmentSender=false` refuses it outright.
A sender that silently degraded to "looks like it worked" is the one thing a chat write must never be.

### The Social Stream Ninja write path

SSN 0.4.18 exposes no dedicated send-message command. The supported write route is its page observation
surface, used as it is:

```text
inspectSourcePage          -> find the chat composer SSN reports as fillable
interactSourcePage fill    -> type the reply
interactSourcePage pressKey-> Enter
```

Details that matter:

- The write side has its **own** named `HttpClient`, its **own** options, its **own** composition root and
  its **own** state. The capture-side command client is never referenced from here, so a stalled or failing
  write cannot exhaust the capture connection pool and writing stays available while reading is degraded.
- Page references are invalidated by SSN on each new inspection, so a stale reference is retried with a
  fresh inspection a bounded number of times.
- The composer is chosen conservatively. An ambiguous field, a page offering only a credential or only a
  search field, or a sole multi-line field with no accessible name is refused rather than guessed at.
- **Nothing is retried after the Enter key.** A failure there may already have posted, so retrying it is
  how a duplicate gets written. Retries before the fill are transport attempts, not additional response
  requests.
- The delivered text is recorded only after the platform accepted it. Recording earlier would suppress the
  echo of a message that was never written.
- A reply longer than the platform ceiling is rejected before the composer is touched.

`inspectSourcePage -> interactSourcePage fill -> interactSourcePage pressKey Enter` is used with SSN's own
`confirm: true` semantics, exactly as the platform requires.

## Configuration

```json
"ChatResponses": {
  "Enabled": false,
  "Sender": "SocialStreamNinja",
  "AllowedProviders": [],
  "MaxCharacters": 500,
  "MessagePrefix": "",
  "MaxQueueSize": 4,
  "GlobalCooldownSeconds": 8,
  "UserCooldownSeconds": 30,
  "CooldownCapacity": 500,
  "IdempotencyCapacity": 500,
  "EchoWindowSeconds": 120,
  "EchoCapacity": 200,
  "SelfActorIdentities": [],
  "SelfActorCapacity": 100,
  "HistoryCapacity": 100,
  "CommandTimeoutSeconds": 15,
  "AllowDevelopmentSender": true
}
```

| Key | Meaning |
|---|---|
| `Enabled` | The single master switch. Ships off. |
| `Sender` | `SocialStreamNinja` or `Development`. |
| `AllowedProviders` | Extra restriction. Empty means any platform the selected sender supports. |
| `MaxCharacters` | Reply length ceiling, counted in runes. Truncation happens on a rune boundary. |
| `MessagePrefix` | Prepended to every reply. Empty means none. |
| `MaxQueueSize` | Bounded queue depth. |
| `GlobalCooldownSeconds` / `UserCooldownSeconds` | Rate limiting. Zero disables rate limiting entirely. |
| `CooldownCapacity` / `IdempotencyCapacity` / `EchoCapacity` / `HistoryCapacity` / `SelfActorCapacity` | Bounded memory. Every one of them is bounded on purpose. |
| `EchoWindowSeconds` | How long a written reply is remembered as our own text. Zero disables duplicate-text suppression and echo matching. |
| `SelfActorIdentities` | `Provider:Account` entries for accounts StudioOS posts from. |
| `CommandTimeoutSeconds` | Hard per-attempt ceiling, covering inspect, fill and Enter as a whole. |
| `AllowDevelopmentSender` | Whether the simulated sender may be selected at all. |

`ChatResponseOptionsValidator` rejects at startup any value that could only fail later — in front of a live
audience — such as an unknown sender name, a zero-capacity ring, a negative cooldown or a prefix longer than
the whole reply budget.

## Runtime settings

Only `Enabled` is mutable at runtime:

```text
GET /api/chat-responses/settings
PUT /api/chat-responses/settings   { "enabled": true }   -> RuntimeOverride
PUT /api/chat-responses/settings   { "reset": true }     -> back to the shipped value
```

Sender selection, provider allow-lists, cooldowns and the queue ceiling stay startup facts, so nothing an
operator can change while a stream is live can alter what a reply is allowed to contain or which platform
it may reach. The override is memory-only, so a stream can never be left with automatic replies silently on.

## APIs

| Endpoint | Purpose |
|---|---|
| `GET /api/chat-responses/providers` | Read and write capability per platform, reported separately. |
| `GET /api/chat-responses/settings` | Effective settings and whether an override is in force. |
| `PUT /api/chat-responses/settings` | Enable/disable, or reset to configuration. |
| `GET /api/chat-responses/state` | Enabled state, queue depth, counters, bounded capacities. |
| `GET /api/chat-responses/recent?limit=` | Bounded history, newest first, with the full correlation chain. |
| `GET /api/chat-responses/senders` | Registered senders, availability and their own counters. |
| `POST /api/chat-responses/dev/send` | **Development only.** One controlled write. |

`GET /api/chat-responses/providers` never infers one side from the other. A connected capture provider says
nothing about whether StudioOS can post, because capture and write use different transports and different
authentication. That is the entire reason this endpoint exists.

`POST /api/chat-responses/dev/send` is not mapped outside Development. It writes operator-supplied text
through the exact production gate, sender and recording path, so a real platform send can be validated
without enabling automatic replies and without a viewer present. It still obeys `ChatResponses:Enabled` —
a gate that can be side-stepped is not a gate — and every result it produces is stamped
`Origin=development-validation`, so it can never be mistaken for a reply to a real viewer message.

## Health

`/health` reports `chatResponses`. **Disabled is `healthy`**: a machine that never opted in is in exactly the
state it asked to be in. `degraded` is reserved for the genuinely broken case — the capability is switched
on but cannot write, which is the situation that would silently drop replies in front of a live audience.

## Not implemented, and deliberately so

- OAuth, stream keys, cookies, API keys or any new secret.
- Official platform APIs (Twitch Helix, YouTube Data API, Kick). `IChatResponseSender` is the seam where a
  future official adapter would be added; none is registered.
- Any database, repository, migration, broker or container.
- Any change to `OBS_CONFIG_ROOT`, OBS sources, scenes, audio or settings.

No reply has been written into a real live chat. Automatic written replies shipped disabled and were never
enabled; see [PROJECT-STATE.md](../PROJECT-STATE.md).
