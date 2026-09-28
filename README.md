# OBS Live Bot / Live Command Center

Plataforma local para automação de live no OBS, IA/TTS opcional, Content Engine, Command Center Windows e backup/restore integral do ambiente OBS.

## Estado
`OBS-LIVE-BOT-00` e `OBS-LIVE-BOT-01` estão concluídas. A task **OBS-LIVE-BOT-02 — OBS WebSocket Connection** implementa o serviço ASP.NET Core, o cliente OBS WebSocket 5.x, reconexão, health check e endpoint de status. A validação real permanece parcial enquanto o modo de autenticação anunciado pelo `Hello` do OBS não corresponder à configuração local.

## Serviços locais

| Serviço | URL |
|---|---|
| OBS Live Bot API | `http://localhost:5080` |
| OBS Live Bot n8n | `http://localhost:5679` |

Endpoints de leitura: `GET /health` e `GET /api/obs/status`.

## Arquitetura
C#/.NET é o núcleo (Vertical Slice, CQRS, Mediator, Domain Events, EF Core, Validation e Mapping). Python é especializado em IA/mídia. C++ é opcional para nativo/performance. n8n é orquestrador.

Leia `PROJECT.md`, `PROJECT-STATE.md`, `AI-WORKFLOW.md` e `docs/ARCHITECTURE.md`.

## Módulos
- Live Engine / AI Content Studio
- Content Engine
- Live Command Center
- Configuration, Backup & Restore

O módulo de backup prevê snapshot integral de `C:\Users\gfmau\AppData\Roaming\obs-studio` e Job automático após o fechamento do OBS.
