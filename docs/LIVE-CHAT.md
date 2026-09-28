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
| Twitch | Foundation | Disabled; NotConfigured se habilitado sem credenciais | NOT EXECUTED |
| YouTube | Foundation | Disabled; NotConfigured se habilitado sem credenciais | NOT EXECUTED |
| TikTok | Foundation, sem scraping/browser automation | Disabled; NotConfigured se habilitado sem integração confiável | NOT EXECUTED |

Os adapters implementam contrato, estado e lifecycle comuns. Credenciais ausentes não impedem startup e resultam em `NotConfigured`; providers desabilitados resultam em `Disabled`. Adapters específicos permanecem em Infrastructure, sem SDKs vazando para Application ou Domain.

## Lifecycle e isolamento

`LiveChatProviderHostedService` inicia apenas providers habilitados, cria loops independentes, respeita cancellation, realiza graceful shutdown e usa backoff `1s, 2s, 5s, 10s, 30s`. Falha de um provider não encerra os outros, o OBS ou a API. `RetryAfter` é respeitado quando o provider o disponibiliza.

`Disabled` e `NotConfigured` são estados saudáveis. Providers habilitados em reconnect, rate limit, authentication failure ou fault deixam somente o health do chat degradado.

## Configuração

Configurações não sensíveis ficam em `LiveChat` no appsettings. Valores sensíveis são lidos apenas de environment variables ou `.env` local ignorado, por exemplo:

- `TWITCH_CLIENT_ID`, `TWITCH_CLIENT_SECRET`, `TWITCH_ACCESS_TOKEN`;
- `YOUTUBE_CLIENT_ID`, `YOUTUBE_CLIENT_SECRET`, `YOUTUBE_REFRESH_TOKEN`.

Nenhum valor fictício é versionado. Social Stream Ninja não é dependência.

## API

- `GET /api/chat/providers`
- `GET /api/chat/state`
- `GET /api/chat/messages?provider=Twitch&limit=50`
- `GET /api/chat/events?limit=20`

`limit` aceita 1–100. Valores numéricos fora da faixa, valores não numéricos e providers desconhecidos retornam HTTP 400. Os endpoints nunca retornam credenciais.

## Fora do escopo

Não há envio de mensagens, IA, TTS, STT, comandos de viewers, comandos OBS via chat, moderação automática, analytics persistentes ou persistência do histórico.
