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
- OBS-LIVE-BOT-09 / 09.3.1 — captura Twitch real via Social Stream Ninja em modo simples, normalização/MediatR/buffer, isolamento, persistência por bind mount, restart e recriação isolada validados; APIs oficiais permanecem opt-in.
- OBS-LIVE-BOT-09.4.1 — PASS: SSN 0.4.18 OAuth YouTube autorizado com Electron `safeStorage`/Secret Service e GNOME Keyring persistente; canal `gfmaurila` detectado; chats reais YouTube e Kick validados por SSE/MediatR/LiveChatBuffer; restart e recriação isolada preservaram a sessão; 259 testes, build limpo e secret scan sem achados. A senha Google permaneceu na página oficial do Google. Narration continua `Degraded` porque o input OBS configurado está ausente; causa confirmada por consulta somente leitura e não introduzida nesta task. Automação permaneceu desligada.
- OBS-LIVE-BOT-09.5 — PASS: backup OBS do profile/collection ETS protegido por DPAPI CurrentUser, manifest/checksums e verificação de rollback; causa histórica da ausência da source permanece desconhecida. `GFM StudioOS - Narration` foi recriada idempotentemente como `ffmpeg_source` e anexada às seis cenas existentes, sem duplicata nem alteração das outras sources. Track 1/MonitorOff, mute/unmute e volume 35% restaurado a 70% validados. Um smoke manual Piper→WAV→OBS completou; health `Ready`; nenhuma live/gravação iniciada; AutoPlayInteractions permaneceu false. 264 testes e build sem erros/avisos.

## Situação
OBS-LIVE-BOT-00 a OBS-LIVE-BOT-09.5 concluídas. Tasks09.3.1/09.4.1 preservaram Twitch real, validaram YouTube e Kick reais, e estabeleceram sessão YouTube segura no SSN. Task09.5 recuperou a source de narração, validou os controles e concluiu smoke real Piper→WAV→OBS; health voltou a `Ready`. Chat permanece input-only com `AutoPlayInteractions=false`. Ver [docs/SOCIAL-STREAM-NINJA.md](docs/SOCIAL-STREAM-NINJA.md) e [docs/NARRATION.md](docs/NARRATION.md).

Subtask OBS-LIVE-BOT-09.3.1 — PASS. A regressão de autenticação OBS foi resolvida ao restaurar no OBS a credencial anterior já esperada pelo StudioOS: `/api/obs/status` voltou a `Connected`, `/api/obs/live-state` voltou a `synchronized=true` e as notificações `OBS_AUTH_FAILED` cessaram. Uma mensagem Twitch real chegou via SSE sem usar o endpoint DEV, foi normalizada como `Twitch/Message` e permaneceu no buffer durante restart e recriação do SSN. Suíte completa: 237 testes; build: 0 erros/0 warnings; secret scan: sem achados.

Follow-up de segurança obrigatório após encerrar a validação diagnóstica da Task09.3.1: rotacionar a credencial OBS WebSocket exposta durante o troubleshooting, atualizando em conjunto o OBS e o armazenamento seguro do StudioOS, sem exibir, registrar ou versionar o novo valor.

## Próxima ação executável
Revisão final, commit e push da Task09.5. OBS-LIVE-BOT-10 não foi iniciada.

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
