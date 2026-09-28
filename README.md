# OBS Live Bot / Live Command Center

Plataforma local para automação de live no OBS, IA/TTS opcional, Content Engine, Command Center Windows e backup/restore integral do ambiente OBS.

## Estado
`OBS-LIVE-BOT-00` a `OBS-LIVE-BOT-05` estão concluídas. A conexão OBS, Live State, Live Chat Ingestion e a fundação local de AI Interaction/TTS possuem estado bounded em memória, validação, health checks e APIs controladas.

## Serviços locais

| Serviço | URL |
|---|---|
| OBS Live Bot API | `http://localhost:5080` |
| OBS Live Bot Swagger | `http://localhost:5080/swagger` |
| OBS Live Bot Swagger UI | `http://localhost:5080/swagger/index.html` |
| OBS Live Bot n8n | `http://localhost:5679` |

## n8n — Desenvolvimento

URL: `http://localhost:5679`

### Owner local de desenvolvimento

Email: `dev@gfmstudio.local`

Senha: `GfmStudioOS@Dev2026`

> **ATENÇÃO:** esta é uma credencial conhecida destinada exclusivamente ao ambiente LOCAL de desenvolvimento do GFM StudioOS. Nunca reutilizar esta senha em produção ou em uma instância exposta externamente.

## Endpoints da API

| Grupo | Método | Endpoint | URL |
|---|---|---|---|
| Health | GET | `/health` | `http://localhost:5080/health` |
| OBS | GET | `/api/obs/status` | `http://localhost:5080/api/obs/status` |
| OBS | GET | `/api/obs/live-state` | `http://localhost:5080/api/obs/live-state` |
| OBS | GET | `/api/obs/events` | `http://localhost:5080/api/obs/events` |
| Live Chat | GET | `/api/chat/providers` | `http://localhost:5080/api/chat/providers` |
| Live Chat | GET | `/api/chat/state` | `http://localhost:5080/api/chat/state` |
| Live Chat | GET | `/api/chat/messages` | `http://localhost:5080/api/chat/messages` |
| Live Chat | GET | `/api/chat/events` | `http://localhost:5080/api/chat/events` |
| Interactions | GET | `/api/interactions/state` | `http://localhost:5080/api/interactions/state` |
| Interactions | GET | `/api/interactions/recent` | `http://localhost:5080/api/interactions/recent` |
| Interactions | GET | `/api/interactions/providers` | `http://localhost:5080/api/interactions/providers` |
| Interactions (Development) | POST | `/api/interactions/dev/test` | `http://localhost:5080/api/interactions/dev/test` |

Endpoints de leitura: `GET /health`, `/api/obs/status`, `/api/obs/live-state`, `/api/obs/events`, `/api/chat/providers`, `/api/chat/state`, `/api/chat/messages`, `/api/chat/events`, `/api/interactions/state`, `/api/interactions/recent` e `/api/interactions/providers`.

## AI Interaction e TTS — Foundation

O subsistema de interações processa eventos normalizados pelo mesmo pipeline de decisão, cooldown, contexto, AI, sanitização, TTS, notificações MediatR, buffer bounded e publisher. Mensagens comuns não recebem resposta automática: nesta etapa, respostas ocorrem somente por solicitação explícita do endpoint DEV ou pelo comando reservado `!studio`.

- `DevelopmentAiInteractionProvider` é determinístico, local e identificado pelo prefixo `[DEV AI]`. Ele **não é uma IA real**.
- `DevelopmentTextToSpeechProvider` retorna somente metadados simulados. Ele **não gera nem reproduz áudio real**.
- Providers reais, Ollama, engines TTS locais e providers externos ainda não estão configurados.
- Nenhuma API paga, Internet, banco, broker, cache distribuído ou novo container é necessário.
- O endpoint `/api/interactions/dev/test` só é registrado quando `ASPNETCORE_ENVIRONMENT=Development`.

Exemplo de teste DEV:

```json
{
  "provider": "Development",
  "channelId": "local",
  "userId": "dev-user",
  "userDisplayName": "Developer",
  "message": "Olá StudioOS",
  "responseMode": "TextAndVoice"
}
```

O resultado `TextAndVoice` confirma a passagem pelo provider TTS DEV, mas não cria arquivo de áudio nem reproduz som na live. Consulte `docs/AI-INTERACTIONS.md` e `docs/TTS.md`.

## Arquitetura
C#/.NET 10 é o núcleo (ASP.NET Core, Vertical Slice, CQRS, MediatR oficial, Ardalis.Result, FluentValidation, Serilog, OpenAPI, Domain Events e Mapping). Python é especializado em IA/mídia. C++ é opcional para nativo/performance. n8n é orquestrador local.

O projeto é local-first e busca custo de infraestrutura zero. EF Core fica disponível quando uma feature realmente exigir persistência, sem provider obrigatório; estado e buffers em memória são preferidos enquanto forem suficientes. Não há Redis, RabbitMQ, Kafka, MongoDB ou banco obrigatório.

Segredos locais ficam somente no `.env` ignorado. O n8n exige `N8N_ENCRYPTION_KEY`, `N8N_HMAC_SIGNATURE_SECRET`, `N8N_BINARY_DATA_SIGNING_SECRET` e `N8N_USER_MANAGEMENT_JWT_SECRET`; seu estado em `data/n8n` também é local, sensível e não versionado.

Leia `PROJECT.md`, `PROJECT-STATE.md`, `AI-WORKFLOW.md` e `docs/ARCHITECTURE.md`.

## Histórico Git sanitizado

O histórico local e a branch remota ativa foram reescritos após a rotação dos segredos do n8n. Clones anteriores à sanitização devem ser substituídos por um clone novo. Para preservar trabalho local legítimo, reaplique somente os commits necessários sobre o clone limpo; não faça merge do histórico antigo.

## Módulos
- Live Engine / AI Content Studio
- Content Engine
- Live Command Center
- Configuration, Backup & Restore

O módulo de backup prevê snapshot integral de `C:\Users\gfmau\AppData\Roaming\obs-studio` e Job automático após o fechamento do OBS.
