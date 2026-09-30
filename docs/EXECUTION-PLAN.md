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
| OBS-LIVE-BOT-08 | OBS Narration & Audio Routing | **PASS — source dedicada, fila FIFO bounded, playback OBS real, routing Track 1/MonitorOff, APIs, regressão e finalização Git verificados** |
| OBS-LIVE-BOT-09 | Twitch Real Chat Integration | **PASS — captura Twitch real via SSN SimpleCapture, pipeline normalizado, isolamento e recuperação validados; official API permanece opt-in e não autenticada** |
| OBS-LIVE-BOT-09.3.1 | Social Stream Ninja Docker + Simple Chat Connection + LiveChat Integration | **PASS — source Twitch `classic` ativa, mensagem real via SSE, persistência após restart/recriação, API preservada, OBS auth sincronizada, 237 testes, build e secret scan** |
| OBS-LIVE-BOT-09.4.1 | YouTube Account OAuth + Secure Session + Real Chat Validation | **PASS — OAuth/SSN safeStorage persistente; YouTube e Kick reais via SSE/LiveChatBuffer; restart/recriação SSN preservou sessão; 259 testes/build/secret scan PASS. Narration Degraded explicado: input OBS ausente, condição anterior e fora do escopo desta task** |
| OBS-LIVE-BOT-09.5 | OBS Narration Source Recovery & Audio Baseline Closure | **PASS — backup protegido DPAPI CurrentUser + manifest/rollback; `ffmpeg_source` recuperada idempotentemente e anexada às seis cenas ETS; Track 1/MonitorOff, mute/volume validados; smoke real Piper→OBS Completed; narration Ready; 264 testes e build limpo** |
| OBS-LIVE-BOT-10 | Automatic Live Chat AI Audio Interaction | planejada; não iniciada; `AutoPlayInteractions=false` |

## Trilhas posteriores já incorporadas à arquitetura
- **Módulo 04 — Content Engine:** transcrição, análise, cortes, áudio contextual e conteúdo multiplataforma.
- **Módulo 05 — Live Command Center:** aplicação Windows centralizadora.
- **Módulo 06 — Configuration, Backup & Restore:** configurações, backup/restore integral do OBS e Job automático no fechamento do OBS.

Essas trilhas deverão ser quebradas em tasks numeradas quando chegarem à execução. `OBS-LIVE-BOT-08` foi concluída; nenhuma task posterior deve ser iniciada sem solicitação explícita.

Todas as tasks seguem `LOCAL FIRST`, `ZERO INFRASTRUCTURE COST` e `NO PREMATURE INFRASTRUCTURE`. Adoção de banco, cache distribuído, broker, cloud ou novo serviço requer necessidade concreta documentada.
