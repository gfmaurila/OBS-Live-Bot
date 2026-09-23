# OBS Live Bot

Automação local e independente para auxiliar transmissões realizadas com o OBS Studio.

Este projeto **não pertence ao GFM TruckHub**. A fundação atual contém somente estrutura, documentação e preparação para a futura execução local do n8n. Nenhuma integração funcional com OBS, chats, TTS ou IA foi implementada.

## Responsabilidades

- **OBS Studio:** transmissão e composição da live.
- **OBS WebSocket:** canal de integração controlada com o OBS Studio.
- **n8n:** orquestração local de eventos e processos.
- **OBS Live Bot:** regras de automação, filas, cooldowns e decisões.
- **TTS:** reprodução de voz, em uma etapa futura.
- **IA:** recurso opcional e substituível, nunca obrigatório para regras locais.
- **Docker:** infraestrutura local para serviços como o n8n.

## Estado atual

Implementado nesta etapa:

- estrutura inicial do projeto;
- documentação de requisitos e arquitetura;
- plano incremental de execução;
- configuração-base do Docker Compose para uso futuro;
- exemplo de variáveis de ambiente sem segredos.

Não implementado nesta etapa:

- execução do n8n;
- conexão com OBS WebSocket;
- alteração de configurações do OBS;
- conectores de YouTube, Twitch ou Kick;
- TTS;
- provedores de IA;
- workflows funcionais.

## Preparação futura

1. Copiar `.env.example` para `.env`.
2. Definir localmente `OBS_WEBSOCKET_PASSWORD`.
3. Manter `.env` fora do Git.
4. Executar a próxima etapa documentada em `docs/EXECUTION-PLAN.md`.

## n8n local

O n8n do OBS Live Bot usa `http://localhost:5679`, vinculado somente a `127.0.0.1`. A porta 5678 permanece reservada ao n8n independente do GFM TruckHub.

```powershell
docker compose up -d
docker compose ps
docker compose logs -f n8n
docker compose down
```

Os dados persistentes ficam em `data\n8n` e não são removidos por `docker compose down`. Não use `docker compose down -v` durante validações de persistência.
