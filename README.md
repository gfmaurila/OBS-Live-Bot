# OBS Live Bot / Live Command Center

Plataforma local para automação de live no OBS, IA/TTS opcional, Content Engine, Command Center Windows e backup/restore integral do ambiente OBS.

## Estado
`OBS-LIVE-BOT-00`, `OBS-LIVE-BOT-01` e **OBS-LIVE-BOT-02 — OBS WebSocket Connection** estão concluídas. A conexão autenticada real com OBS WebSocket 5.x, reconexão, health check e endpoint de status foram validados.

## Serviços locais

| Serviço | URL |
|---|---|
| OBS Live Bot API | `http://localhost:5080` |
| OBS Live Bot n8n | `http://localhost:5679` |

Endpoints de leitura: `GET /health` e `GET /api/obs/status`.

## Arquitetura
C#/.NET 10 é o núcleo (ASP.NET Core, Vertical Slice, CQRS, MediatR oficial, Ardalis.Result, FluentValidation, EF Core SQL Server, Serilog, OpenAPI, Domain Events e Mapping). Python é especializado em IA/mídia. C++ é opcional para nativo/performance. n8n é orquestrador.

Leia `PROJECT.md`, `PROJECT-STATE.md`, `AI-WORKFLOW.md` e `docs/ARCHITECTURE.md`.

## Módulos
- Live Engine / AI Content Studio
- Content Engine
- Live Command Center
- Configuration, Backup & Restore

O módulo de backup prevê snapshot integral de `C:\Users\gfmau\AppData\Roaming\obs-studio` e Job automático após o fechamento do OBS.
