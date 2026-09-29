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
