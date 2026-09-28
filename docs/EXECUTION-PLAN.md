# Plano de execução

Cada task é executada e validada isoladamente. Documentar módulos futuros não autoriza antecipá-los.

| Task | Nome | Estado/resultado |
|---|---|---|
| OBS-LIVE-BOT-00 | Project Foundation | PASS |
| OBS-LIVE-BOT-01 | Docker + n8n local | PASS |
| OBS-LIVE-BOT-02 | OBS WebSocket Connection | **PASS — autenticação, leituras, endpoint, health e reconexão real validados** |
| OBS-LIVE-BOT-03 | Live State Detection | **PASS — subscriptions, sincronização, eventos, ordering, deduplicação e APIs validados** |
| OBS-LIVE-BOT-04 | Live Chat Ingestion | **PASS — providers foundation, pipeline normalizado, buffer/dedup limitados, MediatR e APIs** |
| OBS-LIVE-BOT-05 | AI Interaction & TTS Foundation | **PASS — decisão/cooldown/contexto, providers DEV, sanitização, TTS simulado, buffer, health e APIs** |
| OBS-LIVE-BOT-06 | Local AI Engine / Ollama Integration | **PASS — IA real local, fallback DEV, timeout/cancelamento, overload bounded, métricas, health e smoke real** |
| OBS-LIVE-BOT-07 | Local TTS Engine | **PASS — Piper local, WAV PCM real e integração Ollama + TTS validados; sem playback OBS** |
| OBS-LIVE-BOT-08 | OBS Narration & Audio Routing | planejada |
| OBS-LIVE-BOT-09 | Human Response Timeout | planejada |
| OBS-LIVE-BOT-10 | Automatic Response Engine | planejada |
| OBS-LIVE-BOT-11 | Optional AI Provider | planejada |
| OBS-LIVE-BOT-12 | YouTube + Twitch + Kick | planejada |
| OBS-LIVE-BOT-13 | OBS Audio Integration | planejada |
| OBS-LIVE-BOT-14 | End-to-End Live Tests | planejada |

## Trilhas posteriores já incorporadas à arquitetura
- **Módulo 04 — Content Engine:** transcrição, análise, cortes, áudio contextual e conteúdo multiplataforma.
- **Módulo 05 — Live Command Center:** aplicação Windows centralizadora.
- **Módulo 06 — Configuration, Backup & Restore:** configurações, backup/restore integral do OBS e Job automático no fechamento do OBS.

Essas trilhas deverão ser quebradas em tasks numeradas quando chegarem à execução. `OBS-LIVE-BOT-07` foi concluída; nenhuma task posterior deve ser iniciada sem solicitação explícita.

Todas as tasks seguem `LOCAL FIRST`, `ZERO INFRASTRUCTURE COST` e `NO PREMATURE INFRASTRUCTURE`. Adoção de banco, cache distribuído, broker, cloud ou novo serviço requer necessidade concreta documentada.
