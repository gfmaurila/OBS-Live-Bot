# Live Chat Ingestion

## Escopo da OBS-LIVE-BOT-04

A camada de ingestão de chat é local, executa no mesmo processo ASP.NET Core e não usa banco, cache distribuído, broker ou serviço cloud.

```text
Provider
  -> Adapter (Infrastructure)
     -> ProviderLiveChatEvent
        -> Normalizer
           -> FluentValidation
              -> deduplicação limitada
                 -> sequence monotônica
                    -> MediatR notification
                       -> LiveChatBuffer
                          -> API read-only
                       -> ILiveChatEventPublisher
                          -> n8n/integrations (futuro)
```

O provider não conhece n8n. `NoOpLiveChatEventPublisher` é a implementação atual e mantém a ingestão independente da disponibilidade do orquestrador.

## Modelo normalizado

`LiveChatEvent` preserva identificadores interno e do provider, provider tipado, canal, usuário normalizado, conteúdo Unicode, timestamps UTC, sequence, correlation e metadata sanitizada. A identidade mínima é `Provider + UserId`; usernames não são tratados como identidade global.

Chaves de metadata relacionadas a password, token, secret, credential ou OAuth são rejeitadas pelo validator e removidas defensivamente pelo normalizer. Conteúdo de mensagens não é traduzido, convertido para ASCII ou truncado silenciosamente.

## Buffer e deduplicação

- `LiveChatBuffer`: thread-safe, em memória, capacidade configurável e padrão 500.
- Ao atingir a capacidade, o evento mais antigo é removido.
- O endpoint de mensagens filtra `Message`; o endpoint de eventos usa o mesmo buffer, evitando armazenamento duplicado.
- A deduplicação usa `Provider + ChannelId + ProviderEventId` quando o ID oficial existe.
- O fallback inclui provider, canal, usuário, timestamp, tipo e conteúdo; nunca usa somente o texto.
- O cache de deduplicação também é limitado e remove a chave mais antiga.
- O histórico pode ser perdido no restart, conforme esperado nesta etapa.

## Providers

| Provider | Adapter | Configuração atual | Ingestão real |
|---|---|---|---|
| Twitch | SocialStreamNinja simple capture; official API opt-in | Captura simples independe de OAuth; API oficial exige `officialApiEnabled: true` | REAL CHAT VALIDATED IN TASK09.3.1 |
| YouTube | SocialStreamNinja simple capture; official API opt-in | Conta OAuth do SSN 0.4.18; senha digitada somente no Google; tokens do owner via Electron safeStorage + Secret Service/GNOME Keyring | REAL CHAT VALIDATED; restart/recreation persistence PASS |
| Kick | SocialStreamNinja simple capture; official API opt-in | Captura simples via source WebSocket; API oficial é opt-in | REAL CHAT VALIDATED |
| TikTok | Foundation, sem scraping/browser automation | Disabled; NotConfigured se habilitado sem integração confiável | NOT EXECUTED |

Os adapters implementam contrato, estado e lifecycle comuns. Credenciais ausentes não impedem startup e resultam em `NotConfigured`; providers desabilitados resultam em `Disabled`. Adapters específicos permanecem em Infrastructure, sem SDKs vazando para Application ou Domain.

## Lifecycle e isolamento

`LiveChatProviderHostedService` inicia apenas providers habilitados, cria loops independentes, respeita cancellation, realiza graceful shutdown e usa backoff `1s, 2s, 5s, 10s, 30s`. Falha de um provider não encerra os outros, o OBS ou a API. `RetryAfter` é respeitado quando o provider o disponibiliza.

`Disabled` e `NotConfigured` são estados saudáveis. Providers habilitados em reconnect, rate limit, authentication failure ou fault deixam somente o health do chat degradado.

## Configuração

Configurações não sensíveis ficam em `LiveChat` no appsettings. Valores sensíveis são lidos apenas de environment variables ou `.env` local ignorado, por exemplo:

- `TWITCH_CLIENT_ID`, `TWITCH_CLIENT_SECRET`, `TWITCH_ACCESS_TOKEN`;
- `YOUTUBE_CLIENT_ID`, `YOUTUBE_CLIENT_SECRET`, `YOUTUBE_REFRESH_TOKEN`.

Nenhum valor fictício é versionado. Social Stream Ninja é um adapter de Infrastructure para captura simples e permanece substituível por outro `ILiveChatProvider`.

## API

- `GET /api/chat/providers`
- `GET /api/chat/state`
- `GET /api/chat/messages?provider=Twitch&limit=50`
- `GET /api/chat/events?limit=20`

`limit` aceita 1–100. Valores numéricos fora da faixa, valores não numéricos e providers desconhecidos retornam HTTP 400. Os endpoints nunca retornam credenciais.

## Fora do escopo

Não há envio de mensagens, IA, TTS, STT, comandos de viewers, comandos OBS via chat, moderação automática, analytics persistentes ou persistência do histórico.

## Social Stream Ninja — simple capture (Task09.3)

SSN runs in its own Docker/headless service. Its SSE events enter through `SocialStreamNinjaLiveChatProvider` in Infrastructure, normalize to the existing provider-neutral event, then pass through MediatR and `LiveChatBuffer`. Unicode, dedupe, provider-scoped identity and unknown-event preservation remain the shared pipeline behavior.

`studioos.socialstream.json` contains the minimum capture configuration only: schema, enabled state, endpoint, channel identifiers and provider enables. It must not contain OAuth values, Client Secrets, access/refresh tokens, stream keys, browser cookies or session data. SSN capture uses public channel/live inputs supported by the selected engine. Google Cloud/Data API is not a simple YouTube capture prerequisite when SSN can capture that live; Twitch/Kick developer apps and a public Kick webhook are likewise not prerequisites when SSN can capture those sources.

`studioos.providers.json` remains intact for official authenticated actions. `officialApiEnabled` explicitly gates official integrations; omitted/false means their `enabled` and public IDs do not activate those adapters. Send-message support is a future `IChatResponseSender` boundary. Automatic AI, platform text response and chat-triggered TTS/narration are all disabled in Task09.3.

Task09.3.1 validated a real Twitch message through SSN SSE, normalization, MediatR and the bounded buffer. Restart and isolated recreation of only the SSN container preserved the configured Twitch `classic` source and left the API process and buffered events intact. External-capture interactions remained ignored, with no AI, TTS or narration execution.

Task09.4 was the historical checkpoint for public `authMode` parsing and case-insensitive rejection of password/token/cookie fields, including Portuguese legacy names. At that checkpoint SSN 0.4.18 could not begin OAuth because secure token storage was unavailable; no password, token, cookie or Windows browser profile was copied and real YouTube/Kick chat was not claimed. Task09.4.1 records the resolved Secret Service setup and the later real YouTube/Kick validation in [SOCIAL-STREAM-NINJA.md](SOCIAL-STREAM-NINJA.md).

## Task10 YouTube route and interaction result

In the validated SSN 0.4.18 headless environment, YouTube capture uses the supported `/live_chat` route for a specific public video ID, then the ordinary SSN SSE → `SocialStreamNinjaLiveChatProvider` → normalization/dedupe → MediatR → `LiveChatBuffer` path. The watch-page/classic route was unreliable in that session; this is not generalized to all SSN deployments. Current-live discovery is not implemented because a reliable supported mechanism was not demonstrated. A real YouTube trigger also completed the audio-only interaction pipeline; no platform text reply was sent. See [AI-INTERACTIONS.md](AI-INTERACTIONS.md).
