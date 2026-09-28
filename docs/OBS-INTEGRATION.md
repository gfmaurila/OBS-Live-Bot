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
- A configuração local possui senha vazia. Por isso as leituras autenticadas reais ficaram bloqueadas; nenhum segredo foi procurado ou extraído.
- Nenhum arquivo em `OBS_CONFIG_ROOT` foi escrito pelo projeto.
