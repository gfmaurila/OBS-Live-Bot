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

## Social chat capture — simple mode (OBS-LIVE-BOT-09.3)

```text
Twitch / YouTube / Kick
  -> dedicated Social Stream Ninja Docker/headless engine
  -> SSE -> SocialStreamNinjaProvider (Infrastructure)
  -> normalization / validation / dedupe -> MediatR -> LiveChatBuffer -> read-only APIs
```

SSN is an Infrastructure adapter, not a domain dependency. Domain/Application continue to depend on `ILiveChatProvider`; a future engine can replace it without changing chat, Interaction, Ollama, Piper or narration. `studioos.socialstream.json` contains the minimum public capture settings and is read from the external read-only mount. SSN simple capture does not require StudioOS OAuth, Google Cloud, official EventSub, a public Kick webhook, cookies, stream keys or secrets when the selected SSN version can read the source without them.

Official platform APIs are an optional advanced mode. The existing public `studioos.providers.json` entries remain available; `officialApiEnabled` must be explicitly true before an official adapter starts. Missing/false means simple mode remains independent of developer app setup. Sending future text uses an `IChatResponseSender` boundary with per-platform implementations; it is not implemented here.

Future product flow: `LiveChat -> InteractionDecisionPolicy -> cooldown / anti-spam / anti-loop -> Ollama -> (IChatResponseSender + Piper -> OBS Narration)`. Preserve identity as `Provider + ProviderUserId`; mark bot messages and exclude them from future prompts. Tasks09.3/09.4 validate input only and keep `AutoPlayInteractions=false`. SSN YouTube owner OAuth stays inside the isolated SSN container; Electron safeStorage/Secret Service protects owner tokens, while StudioOS receives normalized chat and public channel metadata. Google passwords never enter StudioOS.

For YouTube simple mode, `authMode=oauth` means SSN owns the provider-controlled browser authorization and encrypted owner-token storage; StudioOS stores only public metadata. SSN 0.4.18 provides owner live discovery, but this run's discovery did not add a source; the active public video URL was added through SSN's supported source API. Linux secure storage uses Electron safeStorage backed by libsecret/Secret Service and GNOME Keyring. This keeps Google credentials out of StudioOS and leaves YouTube authorization failures isolated from Twitch and Kick.

The future Command Center presents each provider, public channel/live URL, `[ Connect ]`, state, last-connect/message times and a sanitized error. Official APIs appear under Advanced as a separate opt-in.

## Optional official Twitch API integration (OBS-LIVE-BOT-09)

```text
`.env:GFM_STUDIOOS_CONFIG_PATH` -> read-only host mount `/app/config`
  -> `studioos.providers.json` (public Twitch / YouTube / Kick settings)
  -> Twitch EventSub WebSocket -> Twitch Infrastructure Provider
  -> normalização/validação/dedupe -> MediatR -> LiveChatBuffer
  -> interação somente via gate explícito (desabilitado por padrão)
  -> Ollama -> Piper -> Narration -> OBS

Windows host: GFM StudioOS Secure Credential Helper -> DPAPI CurrentUser
Docker Linux API: token em memória durante chamadas, sem persistência plaintext
```

O API permanece no Docker. A pasta de configuração pública do host é montada somente para leitura; o caminho host vem de `GFM_STUDIOOS_CONFIG_PATH` e o código usa `/app/config`, sem caminho Windows hardcoded. O JSON é carregado no startup (sem hot reload); se estiver ausente ou inválido, a API continua operacional, registra somente um motivo sanitizado e mantém integrações de providers desabilitadas. Segredos não são aceitos no JSON. Como Windows DPAPI e named pipes do host não estão disponíveis diretamente ao container Linux nesta topologia, o helper genérico usa uma interface HTTP no gateway Docker, com autenticação IPC e allowlist de rede; peer address e firewall precisam de validação antes de habilitar o helper. Tokens persistidos são protegidos pelo helper no perfil Windows; indisponibilidade do helper degrada somente autenticação Twitch. Consulte [docs/TWITCH.md](TWITCH.md).

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

## AI Interaction & TTS Foundation (OBS-LIVE-BOT-05)

```text
LiveChatEvent / DEV request
  -> FluentValidation + MediatR command
     -> deterministic decision policy
        -> provider-scoped loop prevention
        -> bounded in-memory cooldown
           -> bounded context builder
              -> selected AI provider
                 -> response sanitizer
                    -> selected TTS provider when requested
                       -> MediatR notifications
                          -> bounded interaction buffer
                             -> IInteractionEventPublisher
```

- Mensagens comuns são ignoradas por padrão; `!studio` e solicitações DEV explícitas podem responder.
- `SystemInstructions` e `UserMessage` permanecem campos estruturalmente separados.
- `DevelopmentAiInteractionProvider` e `DevelopmentTextToSpeechProvider` continuam como adapters determinísticos para testes/fallback; não representam IA ou áudio reais.
- `IAiInteractionProvider` seleciona Ollama local; `ITextToSpeechProvider` seleciona Piper local real ou Development. Implementações concretas, HTTP/processos e configuração pertencem a Infrastructure.
- Piper recebe texto sanitizado por stdin e argumentos separados, aplica timeout/cancelamento e concorrência/fila bounded e gera WAV temporário em diretório dedicado. O InteractionBuffer guarda metadados/caminho, não blobs de áudio.
- O buffer padrão mantém 100 resultados, com oldest eviction e sequence monotônica. Cooldown também é thread-safe, bounded e provider-scoped.
- Falhas de AI, TTS e publisher são representadas no resultado/buffer sem derrubar OBS, chat, API ou n8n.
- Não há memória persistente, banco, Redis, broker, cloud, execução automática de OBS ou reprodução automática de áudio.
- `IInteractionEventPublisher` usa `NoOpInteractionEventPublisher`; integrações com n8n, UI, SignalR e OBS permanecem futuras.

## OBS Narration & Audio Routing (OBS-LIVE-BOT-08)

```text
InteractionCompletedNotification (somente AutoPlayInteractions=true)
  ou POST /api/narration/dev/test (somente Development)
    -> INarrationService / bounded FIFO
       -> TTS artifact validation + lease
          -> IAudioPlaybackService
             -> IObsRequestClient (mesmo ObsWebSocketClient/conexão)
                -> OBS ffmpeg_source Media Source
```

- `InteractionOrchestrator` não conhece OBS. `INarrationService` mantém fila FIFO bounded em memória, um único reader/playback, estados, eventos MediatR e buffer limitado; fila cheia é rejeitada explicitamente.
- `AutoPlayInteractions` inicia `false`. Inicialização verifica/limpa somente a source própria se ela já existir; não cria source nem reproduz áudio histórico. O endpoint DEV gera texto controlado por Piper e submete à mesma fila; não recebe path.
- `GFM StudioOS - Narration` usa o input OBS built-in `ffmpeg_source`, compartilhado como scene item nas cenas existentes. O idempotent `EnsureSourceAsync` reusa uma source compatível, cria apenas se ausente e corrige anexos faltantes; um tipo incompatível com o mesmo nome falha sem apagar ou substituir a source. O arquivo de runtime é validado no container e seu nome GUID é mapeado para o diretório host Windows configurado; paths arbitrários são rejeitados.
- A source só recebe áudio na Track 1, sem monitoring. No profile/collection observados (`ETS`), `TrackIndex=1` e `RecTracks=1`; assim a configuração atende a saída stream e a gravação configurada. Não foi iniciada live nem gravação local no smoke.
- A fila não interrompe narração atual. `GetMediaInputStatus` confirma `Playing` e `Ended`; timeout protege início/fim. Falha/disconnect vira resultado Failed sem replay automático, enquanto o processo API e subsistemas independentes continuam ativos.
- O provider Piper mantém artifact lease enquanto enfileirado/tocando para que cleanup por tempo/quantidade não remova áudio em uso. A lease é liberada ao encerrar a tentativa.
- Mute e volume controlam somente a source de narração. Valores são limitados a 0–100%; default 70%. Monitoring fixo `MonitorOff` evita retorno/eco por duplicação de monitoramento.
- Implementado: source/media playback, API, health, bounded queue/events e controls. Desenvolvimento: POST de smoke é exposto só em `Development`. Futuro: autoplay deve ser ativado explicitamente; UI/n8n publishers e ajustes de roteamento adicionais não fazem parte desta task.

## Local AI Engine / Ollama (OBS-LIVE-BOT-06)

```text
Application: IAiInteractionProvider
             ^
             |
Infrastructure: InteractionProviderRegistry
               -> OllamaAiInteractionProvider -> HttpClientFactory -> Ollama local
               -> DevelopmentAiInteractionProvider (fallback explícito)
```

- Ollama permanece um runtime local do host Windows, independente de n8n; não foi criado container de IA.
- Application recebe somente contratos de AI e não conhece HTTP, endpoints ou payloads do Ollama.
- Mensagens `system`, contexto e conteúdo `user` são enviados como roles separados; conteúdo do chat nunca é promovido a instrução de sistema.
- `SemaphoreSlim` limita a uma inferência e a espera é bounded em duas requisições/2 segundos por padrão. Overload retorna `OLLAMA_BUSY`.
- Timeout e cancelamento chegam ao `HttpClient`; erros não derrubam API, OBS, Chat ou n8n.
- O fallback Development é configurável, aparece no `InteractionResult` e nunca mascara o health `Degraded` do provider principal.
- Estado/health expõem disponibilidade, modelo e contadores sem prompts, conteúdo do usuário ou secrets.
- A voz selecionada é `pt_BR-faber-medium`; saída WAV PCM 22.050 Hz, mono, 16-bit. Estado do provider e resultado/fallback são identificados sem expor caminhos internos desnecessários.
- Piper indisponível degrada apenas Interactions. Fallback Development, quando habilitado, é identificado e não invalida o texto AI.
- **REAL LOCAL AI: YES; REAL LOCAL TTS: YES; OBS AUDIO PLAYBACK: NOT YET.** Roteamento/playback no OBS não faz parte da Task 07.
