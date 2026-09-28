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

## Próxima task executável
**OBS-LIVE-BOT-05 — AI Interaction & TTS Foundation.** Não iniciada automaticamente.

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
