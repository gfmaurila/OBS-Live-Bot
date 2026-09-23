# Integração com OBS

## Princípio

O OBS Studio é responsável pela transmissão. O OBS Live Bot deve integrar-se por meio do OBS WebSocket, evitando acesso direto ou alterações nos arquivos internos do OBS.

## Configuração prevista

```dotenv
OBS_CONFIG_ROOT=C:\Users\gfmau\AppData\Roaming\obs-studio
OBS_HOST=host.docker.internal
OBS_WEBSOCKET_PORT=4455
OBS_WEBSOCKET_PASSWORD=
```

- `OBS_CONFIG_ROOT` é somente uma referência local e deve permanecer **read-only**.
- `OBS_HOST` permite que um container futuro alcance o host Windows.
- `OBS_WEBSOCKET_PORT` usa a porta padrão esperada.
- `OBS_WEBSOCKET_PASSWORD` deve ser definida apenas no `.env` local.

## Proteções

- O Compose não monta `OBS_CONFIG_ROOT` no container.
- Nenhum script deve editar configurações do OBS.
- Cenas, profiles, plugins, scripts, áudio, fontes e scene collections estão fora do escopo de escrita.
- A futura conexão deverá falhar explicitamente quando host, porta ou senha forem inválidos.
- Operações futuras no OBS devem ser mínimas, auditáveis e autorizadas pela task correspondente.

## Estado atual

Não existe conexão com OBS WebSocket e nenhuma alteração foi realizada no OBS durante `OBS-LIVE-BOT-00`.

