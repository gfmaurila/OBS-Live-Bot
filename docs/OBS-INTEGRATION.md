# Integração com OBS

## Fluxo

```text
OBS Live Bot
    ↓
ObsConnectionManager / IObsClient
    ↓
ObsWebSocketClient
    ↓
OBS WebSocket 5.x
    ↓
OBS Studio
```

O n8n permanece apenas como orquestrador. A conexão, autenticação, leitura e reconexão pertencem ao serviço C#.

## Configuração

```dotenv
OBS_WEBSOCKET_HOST=host.docker.internal
OBS_WEBSOCKET_PORT=4455
OBS_WEBSOCKET_PASSWORD=
```

- Em Docker, o host padrão é `host.docker.internal`.
- Em execução direta no Windows, o padrão é `localhost`.
- A senha é opcional. Valor vazio não invalida configuração nem impede startup.
- Quando o `Hello` não possui `authentication`, o cliente envia `Identify` sem autenticação.
- Quando o `Hello` possui `authentication`, uma senha configurada é transformada segundo o challenge/salt oficial; a senha nunca é enviada ou registrada em claro.
- Se o servidor exige autenticação e a senha está vazia, o estado é `AuthenticationFailed`, sem crash ou vazamento.

## Leituras implementadas

- `GetVersion` (`obsVersion` e `obsWebSocketVersion`)
- `GetCurrentProgramScene`
- `GetStreamStatus`
- `GetRecordStatus`, incluindo `outputPaused`

Não existem comandos para iniciar/parar stream, gravação ou alterar cena nesta task.

## Estado e reconexão

Estados: `Disconnected`, `Connecting`, `Connected`, `Reconnecting`, `AuthenticationFailed` e `Faulted`.

O backoff é `1s, 2s, 5s, 10s, 30s`, permanecendo limitado a 30 segundos. OBS offline é estado operacional: a API continua viva e o health check retorna `Degraded`. Configuração estrutural inválida ou falha de autenticação retorna `Unhealthy`.

Logs operacionais: `OBS_CONNECTING`, `OBS_CONNECTED`, `OBS_DISCONNECTED`, `OBS_RECONNECTING` e `OBS_AUTH_FAILED`.

## API

- `GET /api/obs/status`: snapshot sanitizado de conexão, versões, cena, stream e gravação.
- `GET /health`: `Healthy` conectado, `Degraded` offline/reconectando e `Unhealthy` para falha estrutural/autenticação.

## Validação do ambiente em 2026-09-27

- OBS Studio instalado: `32.1.2`.
- Host Docker: `host.docker.internal`.
- Porta configurada/escutando: `4455`.
- O `Hello` real informou OBS WebSocket `5.7.3` e anunciou autenticação.
- A credencial comprometida foi rotacionada com 32 bytes aleatórios, armazenada somente no `.env` ignorado e aplicada ao OBS com o programa fechado.
- Antes da alteração foi criado backup protegido por Windows DPAPI do arquivo específico do OBS WebSocket.
- A autenticação e as leituras reais retornaram OBS `32.1.2`, WebSocket `5.7.3`, cena atual, streaming e recording.
- O teste real confirmou `Connected -> Reconnecting/Degraded -> Connected/Healthy`, sem queda da API.
- Fora da rotação explicitamente autorizada do arquivo WebSocket, nenhuma cena, profile, source, scene collection ou configuração operacional do OBS foi alterada.

## Detecção de estado em tempo real

O `Identify` assina somente as categorias necessárias: General, Config, Scenes, Inputs, Outputs, SceneItems, InputActiveStateChanged e InputShowStateChanged. Eventos `op=5` entram em um `Channel` ordenado e são interpretados pela Application.

O estado central acompanha conexão/sincronização, cena, collection, profile, stream, recording, replay buffer e câmera virtual. Mudanças de sources, visibilidade, mute e volume são publicadas como notifications do MediatR e armazenadas no buffer limitado de inspeção.

Em cada conexão ou reconnect ocorre sincronização completa por requests de leitura. Durante indisponibilidade, o último estado é preservado com `stale=true` e `synchronized=false`; após reconexão, um novo `ConnectionId` e uma nova sequence identificam a sessão.

Endpoints:

- `GET /api/obs/live-state`
- `GET /api/obs/events?limit=20` (`limit` entre 1 e 100)

O teste real detectou a transição controlada `Iniciando -> Finalizando -> Iniciando`, restaurou a cena original e confirmou full resync após reinício do OBS com stream e recording inativos.

## Diagnóstico de autenticação da Task09.3.1 — 2026-09-29

- Servidor OBS WebSocket habilitado na porta `4455`, com autenticação obrigatória.
- Credencial do StudioOS carregada e correspondente à credencial restaurada no OBS, sem exibição do valor.
- `GET /api/obs/status`: `Connected`.
- `GET /api/obs/live-state`: `synchronized=true` e `stale=false`.
- Após `OBS_CONNECTED`, as notificações repetidas `OBS_AUTH_FAILED` cessaram.
- Causa raiz comprovada: a credencial OBS WebSocket havia mudado enquanto o StudioOS ainda mantinha/esperava a credencial anterior.
- Nenhuma rotação foi executada durante o diagnóstico e a credencial do StudioOS não foi alterada.
- Follow-up obrigatório após a Task09.3.1: rotacionar a credencial exposta e atualizar OBS e armazenamento seguro do StudioOS em conjunto, sem imprimir, registrar, documentar ou versionar o novo valor.

## OBS-LIVE-BOT-10.1.2 — consumo de live-state pelo reconciliador do YouTube

O `YouTubeLiveOrchestrator` passou a ser um consumidor de `IReadOnlyLiveStateTracker`. Ele **apenas lê** o estado de streaming/gravação do OBS; nenhuma transição de cena, source, filtro ou áudio é disparada, e o reconciliador nunca altera o OBS. O estado atual do OBS é a verdade: se o OBS não está transmitindo, nenhuma fonte do YouTube é criada ou iniciada.

O worker reage a três-fontes: no startup, para convergir um restart do StudioOS durante uma live já em andamento; a cada sinal de live-state, coalescido para não gerar trabalho duplicado; e em um tick periódico, porque o OBS pode permanecer transmitindo através de uma troca de live do YouTube sem emitir sinal algum. O tick é limitado pelo mesmo `minDiscoveryIntervalSeconds`, e cada reconciliação é idempotente, então um disparo sem trabalho custa quase nada.

Validação em live real, sem interromper a transmissão: o estado do OBS permaneceu `Connected`, cena em programa `Iniciando`, `streaming=true` e `recording=true` durante toda a execução, incluindo a recriação do container da API. As tentativas de descoberta avançaram com o tick, sem churn de fonte e sem duplicata.

## Dívida técnica do ambiente OBS — registrada na Task10.1.1

Diagnóstico somente leitura, sem nenhuma alteração no OBS, no Ulanzi, no streaming ou na gravação. Estes itens **não** pertencem ao pipeline de narração do StudioOS, **não** bloqueiam o E2E do Ollama Docker e **não** devem ser reparados sem task explícita.

A source `GFM StudioOS - Narration` foi verificada e está correta: `ffmpeg_source` presente e habilitada nas seis cenas, inclusive na cena em programa, `muted=false`, volume 70% (`-3,10 dB`), `MonitorOff`, Track 1. Todas as saídas ativas e a gravação usam a Track 1, portanto a narração está incluída na mixagem de streaming. `MonitorOff` impede apenas o monitoramento local e **não** impede o envio para o stream.

1. **Bindings de captura de processo do Discord obsoletos.** As fontes `discord - geral`, `discord - ets`, `discord - ats` e `discord - latinos - 1/2/3` guardam a assinatura `título:classe:exe` de janelas antigas. O título vivo do Discord não corresponde mais ao armazenado, portanto o bind não voltará a ocorrer. Exige re-seleção explícita de cada janela no OBS.
2. **Captura de navegador/música aponta para aba inexistente.** `Nagedador-musica` referencia uma aba do Chrome que não existe mais. Mesma causa e mesmo requisito de re-seleção.
3. **Microfone com GUID de dispositivo ausente.** `microfone` referencia um `device_id` que não está presente entre os dispositivos de áudio atuais; a inicialização falha com `Failed to enumerate device`. Exige re-seleção explícita de dispositivo.
4. **Captura de áudio de jogo requer revisão separada.** As fontes de áudio de jogo existentes no boot não estão mais presentes na lista de inputs; os jogos aparecem apenas como `game_capture` de vídeo. Requer decisão explícita sobre se o áudio do jogo deve ser capturado.
5. **Gravação reporta ativa com MKV de 0 bytes.** `GetRecordStatus` reporta `outputActive=true`, porém o arquivo MKV da sessão permanece com 0 bytes por horas, com `obs-ffmpeg-mux` ativo. Requer investigação dedicada; não foi tocada nesta task.

### Origem das falhas de bind

Fontes `wasapi_process_output_capture` do OBS 30+ resolvem o alvo por assinatura de janela e **não re-tentam** quando o aplicativo alvo só aparece depois do startup do OBS. Um bind que falhou no boot permanece morto até re-seleção manual da janela ou reinício do OBS com o aplicativo já em execução. Reiniciar o OBS derrubaria a transmissão ao vivo e, portanto, não é um caminho aceitável durante uma live.

### Nota de interpretação

O `Ulanzi` é um sistema de atalhos/controller de hardware, equivalente a hotkeys que disparam ações no OBS/StudioOS. Ele **não** participa do pipeline de áudio e **não** é a voz do chat. Nenhuma alteração foi aplicada nele. A futura voz do chat será implementada dentro do StudioOS, de forma independente, reutilizando a arquitetura de TTS/narração já existente.
