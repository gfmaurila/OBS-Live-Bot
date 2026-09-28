# OBS Live Bot / Live Command Center

Plataforma local para automação de live no OBS, IA/TTS opcional, Content Engine, Command Center Windows e backup/restore integral do ambiente OBS.

## Estado
`OBS-LIVE-BOT-00` a `OBS-LIVE-BOT-04` estão concluídas. A conexão OBS, Live State e a fundação local de Live Chat Ingestion possuem estado em memória, lifecycle isolado, validação, deduplicação, health checks e APIs read-only.

## Serviços locais

| Serviço | URL |
|---|---|
| OBS Live Bot API | `http://localhost:5080` |
| OBS Live Bot Swagger | `http://localhost:5080/swagger` |
| OBS Live Bot Swagger UI | `http://localhost:5080/swagger/index.html` |
| OBS Live Bot n8n | `http://localhost:5679` |

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

Endpoints de leitura: `GET /health`, `/api/obs/status`, `/api/obs/live-state`, `/api/obs/events`, `/api/chat/providers`, `/api/chat/state`, `/api/chat/messages` e `/api/chat/events`.

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
