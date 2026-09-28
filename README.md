# OBS Live Bot / Live Command Center

Plataforma local para automação de live no OBS, IA/TTS opcional, Content Engine, Command Center Windows e backup/restore integral do ambiente OBS.

## Estado
`OBS-LIVE-BOT-00` a `OBS-LIVE-BOT-04` estão concluídas. A conexão OBS, Live State e a fundação local de Live Chat Ingestion possuem estado em memória, lifecycle isolado, validação, deduplicação, health checks e APIs read-only.

## Serviços locais

| Serviço | URL |
|---|---|
| OBS Live Bot API | `http://localhost:5080` |
| OBS Live Bot n8n | `http://localhost:5679` |

Endpoints de leitura: `GET /health`, `/api/obs/status`, `/api/obs/live-state`, `/api/obs/events`, `/api/chat/providers`, `/api/chat/state`, `/api/chat/messages` e `/api/chat/events`.

## Arquitetura
C#/.NET 10 é o núcleo (ASP.NET Core, Vertical Slice, CQRS, MediatR oficial, Ardalis.Result, FluentValidation, Serilog, OpenAPI, Domain Events e Mapping). Python é especializado em IA/mídia. C++ é opcional para nativo/performance. n8n é orquestrador local.

O projeto é local-first e busca custo de infraestrutura zero. EF Core fica disponível quando uma feature realmente exigir persistência, sem provider obrigatório; estado e buffers em memória são preferidos enquanto forem suficientes. Não há Redis, RabbitMQ, Kafka, MongoDB ou banco obrigatório.

Leia `PROJECT.md`, `PROJECT-STATE.md`, `AI-WORKFLOW.md` e `docs/ARCHITECTURE.md`.

## Módulos
- Live Engine / AI Content Studio
- Content Engine
- Live Command Center
- Configuration, Backup & Restore

O módulo de backup prevê snapshot integral de `C:\Users\gfmau\AppData\Roaming\obs-studio` e Job automático após o fechamento do OBS.
