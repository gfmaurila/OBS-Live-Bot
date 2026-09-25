# OBS Live Bot / Live Command Center

Plataforma local para automação de live no OBS, IA/TTS opcional, Content Engine, Command Center Windows e backup/restore integral do ambiente OBS.

## Estado
`OBS-LIVE-BOT-00` e `OBS-LIVE-BOT-01` concluídas. Próxima task: **OBS-LIVE-BOT-02 — OBS WebSocket Connection**.

## Arquitetura
C#/.NET é o núcleo (Vertical Slice, CQRS, Mediator, Domain Events, EF Core, Validation e Mapping). Python é especializado em IA/mídia. C++ é opcional para nativo/performance. n8n é orquestrador.

Leia `PROJECT.md`, `PROJECT-STATE.md`, `AI-WORKFLOW.md` e `docs/ARCHITECTURE.md`.

## Módulos
- Live Engine / AI Content Studio
- Content Engine
- Live Command Center
- Configuration, Backup & Restore

O módulo de backup prevê snapshot integral de `C:\Users\gfmau\AppData\Roaming\obs-studio` e Job automático após o fechamento do OBS.
