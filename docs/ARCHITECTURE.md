# Arquitetura do Sistema

## Visão geral

```text
                 Live Command Center (Windows / C#)
                            |
                    ASP.NET Core / CQRS
                            |
          +-----------------+------------------+
          |                 |                  |
     Live Engine       Content Engine     Config/Backup
        C#                C# + Python          C#
          |                 |                  |
 OBS WebSocket/chat     mídia/IA workers   OBS full backup
          |                 |                  |
          +----------- Mediator/Domain --------+
                            |
                  Infrastructure adapters
                  |        |        |
                 n8n     Python     C++
             orchestration AI/media native/perf
```

## Stack C# oficial
- .NET 10 + ASP.NET Core
- Vertical Slice Architecture
- CQRS
- MediatR (pacote oficial; sem implementação própria)
- Ardalis.Result
- FluentValidation
- Domain Model
- Domain Events
- Entity Framework Core disponível quando necessário, sem provider obrigatório
- Validation via pipeline
- Mapping entre Request/Command/Domain/Response
- Dependency Injection
- Serilog structured logging
- Swagger / OpenAPI
- Docker + Docker Compose

## Política de infraestrutura

- Desenvolvimento `LOCAL FIRST`, com `ZERO INFRASTRUCTURE COST`.
- Usar estado e buffers thread-safe em memória enquanto uma única instância local atender ao requisito.
- Não criar DbContext, migrations, repositories ou banco preventivamente.
- Quando persistência relacional simples e local for necessária, avaliar SQLite primeiro; para cenários maiores/multiusuário, avaliar PostgreSQL.
- Redis, RabbitMQ, Kafka, MongoDB, cloud e novos containers exigem problema concreto e justificativa técnica.
- MediatR é o mecanismo de eventos internos no mesmo processo; n8n permanece orquestrador local.
- Evitar microservices prematuros e usar o componente mais simples que satisfaça a feature.

## Fluxo de slice
```text
Endpoint -> Request -> Mapping -> Command/Query -> Mediator
  -> Validation/Logging/Transaction Behaviors -> Handler
  -> Domain + Infrastructure -> Mapping -> Response
```

Commands alteram estado. Queries somente leem. Domain Events representam fatos relevantes já ocorridos e permitem efeitos desacoplados. Endpoints não contêm regra de negócio. Entidades de domínio não são contratos externos.

## Ownership
### C#/.NET
Fonte de verdade do domínio, API, Command Center Windows, OBS WebSocket, regras de live, settings, persistência, backup/restore, contratos e coordenação.

### Python
Workers especializados: transcrição, IA/ML, análise de áudio/vídeo, detecção/classificação de momentos, geração de metadata e outros workloads do ecossistema Python. Não é dono do domínio/banco principal/configuração global.

### C++
Opcional. Somente integração nativa, plugin/extensão OBS, áudio de baixa latência ou processamento pesado com necessidade comprovada. Sem regras de negócio.

### n8n
Orquestra webhooks, schedules e integrações. Não guarda a verdade do domínio e não substitui handlers C#.

## Estrutura alvo
```text
src/
  dotnet/
    ObsLiveBot.CommandCenter/
    ObsLiveBot.Api/
    ObsLiveBot.Application/
    ObsLiveBot.Domain/
    ObsLiveBot.Infrastructure/
    ObsLiveBot.Contracts/
    Features/
      Obs/
      Live/
      Content/
      Settings/
      Backup/
    Shared/
      CQRS/
      Behaviors/
      DomainEvents/
      Mapping/
      Results/
  python/
    content_engine/
    ai_engine/
    shared/
    tests/
  cpp/
    native/
    audio/
    media/
    obs/
    include/
    tests/
n8n/workflows/
config/schemas/
data/
tests/integration/
tests/e2e/
```

## Módulos
1. OBS/Live Engine — conexão, estado, chat, fila, anti-spam, TTS, respostas e IA opcional.
2. Content Engine — ingestão, transcrição, detecção de momentos, cortes, áudio contextual/divertido e conteúdo para YouTube/TikTok/Instagram.
3. Live Command Center — aplicação visual Windows que centraliza status e configuração de OBS, live, chat, IA/TTS, conteúdo, n8n e backup.
4. Configuration, Backup & Restore — export/import das configurações do produto e backup/restore integral do ambiente OBS.

## Regra OBS_CONFIG_ROOT
Read-only por padrão. Backup pode ler/copiar toda a árvore. Restore/escrita somente em task explícita, com OBS fechado, validação, backup de segurança e rollback.

## Integração OBS WebSocket (OBS-LIVE-BOT-02)

```text
n8n (orquestrador)
  -> OBS Live Bot API
     -> Query/Mediator -> IObsClient
        -> ObsConnectionManager
           -> ObsWebSocketClient
              -> OBS WebSocket 5.x
                 -> OBS Studio
```

- `ObsLiveBot.Domain` contém estado de conexão, snapshot de runtime e eventos, sem dependência WebSocket.
- `ObsLiveBot.Application` contém contratos, query/handler, pipeline FluentValidation e integração com MediatR oficial.
- `ObsLiveBot.Infrastructure` possui Options, protocolo WebSocket, connection manager, reconexão e health check.
- `ObsLiveBot.Api` expõe somente leitura em `/api/obs/status` e `/health`.
- A conexão é única e reutilizável; operações não abrem sockets independentes.
- Eventos `ObsConnected`, `ObsDisconnected`, `ObsReconnecting` e `ObsConnectionFailed` permitem extensão desacoplada.

## Live State Detection (OBS-LIVE-BOT-03)

```text
OBS WebSocket 5.x
  -> Event Adapter (op=5)
     -> Channel ordenado, single-reader
        -> Application / MediatR
           -> ObsLiveState thread-safe
              -> buffer limitado de eventos
                 -> API read-only
                    -> ILiveEventPublisher (bridge futuro)
                       -> n8n (futuro, somente orquestração)
```

- O socket apenas clona/enfileira eventos; interpretação e publicação não bloqueiam o receive loop.
- O estado usa snapshots imutáveis protegidos para leituras concorrentes e serialização ordenada das transições.
- Cada envelope possui `EventId`, UTC timestamp, `ConnectionId`, correlation, sequence e payload sanitizado.
- A sequence é monotônica por conexão. Um reconnect cria novo `ConnectionId`, preserva o último snapshot como stale e executa full resync antes de voltar a synchronized.
- Eventos equivalentes ao estado atual são deduplicados.
- O buffer mantém no máximo 100 eventos em memória; não há persistência nesta task.

## Live Chat Ingestion (OBS-LIVE-BOT-04)

```text
Twitch / YouTube / TikTok / futuros providers
  -> adapters isolados em Infrastructure
     -> normalização + FluentValidation
        -> deduplicação limitada + sequence
           -> MediatR
              -> LiveChatBuffer limitado em memória
                 -> APIs read-only
              -> ILiveChatEventPublisher
                 -> n8n futuro, sem acoplamento
```

- Domain e Application não dependem de SDKs de providers.
- O registry usa `LiveChatProviderType`, sem service locator genérico ou strings espalhadas.
- O hosted service isola lifecycle, reconnect e falhas por provider.
- O buffer padrão contém 500 eventos e remove o mais antigo ao atingir a capacidade.
- Mensagens e eventos compartilham o mesmo armazenamento; endpoints aplicam filtros de leitura.
- Não existe persistência, database, Redis, broker, novo microservice ou integração cloud.
