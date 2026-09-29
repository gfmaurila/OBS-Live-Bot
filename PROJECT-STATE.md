# PROJECT STATE

## Projeto
OBS Live Bot / Live Command Center

## Situação
IN_PROGRESS

## Concluído
- OBS-LIVE-BOT-00 — Project Foundation.
- OBS-LIVE-BOT-01 — Docker + n8n local: container, porta 5679, healthcheck, isolamento, persistência, restart e recreation validados.
- OBS-LIVE-BOT-02 — conexão autenticada OBS WebSocket 5.x, leituras de runtime, reconexão real, health check, endpoint e testes validados.
- OBS-LIVE-BOT-03 — subscriptions OBS, Live State thread-safe, sincronização/reconexão, eventos MediatR, buffer limitado e endpoints validados.
- OBS-LIVE-BOT-04 — ingestão normalizada de chat, registry/lifecycle isolado de providers, validação/deduplicação, buffer em memória limitado, eventos MediatR e APIs read-only.
- OBS-LIVE-BOT-04.1 — remediação do encryption key do n8n: rotação local, runtime fora do Git, backup protegido e saneamento do histórico local.
- OBS-LIVE-BOT-04.2 — remediação do histórico remoto: refs ativas auditadas e substituídas pelo histórico sanitizado, com recovery point protegido e orientação para clones existentes.
- OBS-LIVE-BOT-04.3 — owner local de desenvolvimento do n8n configurado no banco persistido correto, sem recriar volume, alterar encryption key, workflows ou credentials.
- OBS-LIVE-BOT-05 — foundation local de AI Interaction e TTS: decisão determinística, prevenção de loop, cooldown e buffers bounded, providers DEV, isolamento de falhas, MediatR, health e APIs validados.
- OBS-LIVE-BOT-06 — IA real local com Ollama: provider Infrastructure substituível, modelo `qwen3:4b-instruct-2507-q4_K_M`, timeout/cancelamento, concorrência e overload bounded, fallback DEV explícito, métricas/health e smoke test real validados.
- OBS-LIVE-BOT-07 — TTS local real com Piper 1.2.0 e voz `pt_BR-faber-medium`: artefato WAV PCM validado e pipeline Ollama + Piper exercitado sem playback OBS; provider DEV, fallback, timeout/cancelamento, concorrência, overload, armazenamento e cleanup bounded preservados.
- OBS-LIVE-BOT-08 — narração local via Media Source dedicada do OBS: fila FIFO bounded, playback serial, source isolada em Track 1/MonitorOff, volume/mute independentes, segurança de artifacts, health/API e smoke real Piper → WAV → OBS concluídos; sem live pública ou gravação.

## Situação
OBS-LIVE-BOT-00 a OBS-LIVE-BOT-08 concluídas e verificadas. A reprodução automática das interações permanece desativada (`AutoPlayInteractions=false`).

## Próxima task executável
A definir após validação da Task 08. Não iniciar task posterior automaticamente; aguardar solicitação explícita.

## Decisão arquitetural permanente
O projeto adota `LOCAL FIRST`, `ZERO INFRASTRUCTURE COST`, `NO PREMATURE INFRASTRUCTURE` e `NO PREMATURE MICROSERVICES`. SQL Server não faz parte da stack obrigatória. EF Core permanece disponível somente quando houver necessidade concreta de persistência e sem provider obrigatório.

A ampliação arquitetural para Content Engine, Command Center e Configuration/Backup/Restore está documentada, mas não autoriza antecipar implementação.

## Módulos planejados
- Live Engine / AI Content Studio.
- Content Engine.
- Live Command Center Windows.
- Configuration, Backup & Restore.

## Continuidade
Não voltar ao scaffold/fundação e não avançar etapas sem solicitação explícita.
