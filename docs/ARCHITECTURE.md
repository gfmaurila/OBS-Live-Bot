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
- ASP.NET Core
- Vertical Slice Architecture
- CQRS
- Mediator
- Domain Model
- Domain Events
- Entity Framework Core + Migrations
- Validation via pipeline
- Mapping entre Request/Command/Domain/Response
- Dependency Injection
- Structured Logging

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
