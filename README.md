# OBS Live Bot / Live Command Center

Plataforma local para automação de live no OBS, IA/TTS opcional, Content Engine, Command Center Windows e backup/restore integral do ambiente OBS.

## Estado
`OBS-LIVE-BOT-00` a `OBS-LIVE-BOT-11` estão concluídas. Task10 teve E2E real de áudio validado pelo YouTube e ouvido pelo analista: SSN `/live_chat` → SSE → interação → Ollama → Piper/WAV → fila → OBS. Task10.1 migrou o Ollama para Docker com modelo persistente e GPU validada, e a Task10.1.1 repetir esse E2E no runtime Docker também foi confirmado pelo analista, que ouviu o áudio real sem qualquer dependência do Ollama do Windows. A Task10.1.2 implementou a descoberta automática do YouTube live a partir da página pública `/streams` do canal, dispensando a URL manual por transmissão, e gerencia a fonte SSN canônica `youtube-vid-<videoId>` por meio de um reconciliador idempotente. A OBS-LIVE-BOT-10.2 implementa duas vozes por interação (Chat determinístico + Assistant gerado) com ordenação de grupo e síntese paralela. A OBS-LIVE-BOT-11 implementa a resposta escrita no chat como capacidade independente atrás da fronteira `IChatResponseSender`, com gate próprio, anti-loop por identidade, idempotência e isolamento de falha: ela **foi entregue e permaneceu desligada**, e nenhuma mensagem foi escrita em uma live real. O autoplay permanece `false`. No ambiente headless testado, a source watch-page/classic não expôs chat confiavelmente, enquanto a rota oficial `live_chat` capturou mensagens reais; isso não é uma afirmação de falha universal do SSN. Dívida técnica de captura de áudio do OBS está registrada em [docs/OBS-INTEGRATION.md](docs/OBS-INTEGRATION.md).

## Serviços locais

| Serviço | URL |
|---|---|
| OBS Live Bot API | `http://localhost:5080` |
| OBS Live Bot Swagger | `http://localhost:5080/swagger` |
| OBS Live Bot Swagger UI | `http://localhost:5080/swagger/index.html` |
| OBS Live Bot n8n | `http://localhost:5679` |
| GFM StudioOS Ollama | `http://ollama:11434` (somente rede Docker) |

## n8n — Desenvolvimento

URL: `http://localhost:5679`

### Owner local de desenvolvimento

Email: `dev@gfmstudio.local`

Use a senha local provisionada no ambiente; credenciais não são documentadas nem versionadas.

## Endpoints da API

| Grupo | Método | Endpoint | URL |
|---|---|---|---|
| Health | GET | `/health` | `http://localhost:5080/health` |
| OBS | GET | `/api/obs/status` | `http://localhost:5080/api/obs/status` |
| OBS | GET | `/api/obs/live-state` | `http://localhost:5080/api/obs/live-state` |
| OBS | GET | `/api/obs/events` | `http://localhost:5080/api/obs/events` |
| Live Chat | GET | `/api/chat/providers` | `http://localhost:5080/api/chat/providers` |
| Live Chat | GET | `/api/chat/state` | `http://localhost:5080/api/chat/state` |
| Live Chat | GET | `/api/chat/messages` | `http://localhost:5080/api/chat/messages` |
| Live Chat | GET | `/api/chat/events` | `http://localhost:5080/api/chat/events` |
| Twitch Auth | GET | `/api/chat/twitch/auth/status` | `http://localhost:5080/api/chat/twitch/auth/status` |
| Twitch Auth | POST | `/api/chat/twitch/auth/start` | `http://localhost:5080/api/chat/twitch/auth/start` |
| Twitch Auth | POST | `/api/chat/twitch/auth/logout` | `http://localhost:5080/api/chat/twitch/auth/logout` |
| Interactions | GET | `/api/interactions/state` | `http://localhost:5080/api/interactions/state` |
| Interactions | GET | `/api/interactions/recent` | `http://localhost:5080/api/interactions/recent` |
| Interactions | GET | `/api/interactions/providers` | `http://localhost:5080/api/interactions/providers` |
| Interactions (Development) | POST | `/api/interactions/dev/test` | `http://localhost:5080/api/interactions/dev/test` |
| Narration | GET | `/api/narration/state` | `http://localhost:5080/api/narration/state` |
| Narration | GET | `/api/narration/recent` | `http://localhost:5080/api/narration/recent` |
| Narration (Development) | POST | `/api/narration/dev/test` | `http://localhost:5080/api/narration/dev/test` |
| Narration | PUT | `/api/narration/mute` | `http://localhost:5080/api/narration/mute` |
| Narration | PUT | `/api/narration/volume` | `http://localhost:5080/api/narration/volume` |
| YouTube Live | GET | `/api/youtube/live-discovery` | `http://localhost:5080/api/youtube/live-discovery` |
| Chat Responses | GET | `/api/chat-responses/providers` | `http://localhost:5080/api/chat-responses/providers` |
| Chat Responses | GET | `/api/chat-responses/senders` | `http://localhost:5080/api/chat-responses/senders` |
| Chat Responses | GET | `/api/chat-responses/settings` | `http://localhost:5080/api/chat-responses/settings` |
| Chat Responses | PUT | `/api/chat-responses/settings` | `http://localhost:5080/api/chat-responses/settings` |
| Chat Responses | GET | `/api/chat-responses/state` | `http://localhost:5080/api/chat-responses/state` |
| Chat Responses | GET | `/api/chat-responses/recent` | `http://localhost:5080/api/chat-responses/recent` |
| Chat Responses (Development) | POST | `/api/chat-responses/dev/send` | `http://localhost:5080/api/chat-responses/dev/send` |

## Captura simples de chat

O padrão do StudioOS é captura simples pelo container dedicado Social Stream Ninja (SSN). Configure somente o canal ou live que o mecanismo suporta; a API recebe o transporte SSE, normaliza os eventos e os publica pelo MediatR no `LiveChatBuffer`. A configuração reside em `studioos.socialstream.json`, fora do Git e montada read-only em `/app/config`.

Captura simples não usa os Client IDs do arquivo `studioos.providers.json`, OAuth do StudioOS, cookies copiados, credenciais do navegador nem webhooks públicos. Para YouTube, o usuário escolhe Connect no SSN, autentica diretamente na página oficial do Google e autoriza o canal; SSN armazena a sessão cifrada com Electron `safeStorage` usando Secret Service/GNOME Keyring. StudioOS nunca coleta ou armazena a senha Google. Google Cloud/YouTube Data API não é pré-requisito para esse fluxo. Da mesma forma, consoles de desenvolvedor Twitch/Kick não são pré-requisito para captura simples quando o SSN suporta o canal ou URL informado.

O arquivo público `studioos.socialstream.json` aceita somente dados não secretos. O YouTube pode declarar `authMode: "oauth"`; campos como `usuario`, `senha`, `password`, Client Secret, tokens, authorization code e cookies são rejeitados de forma controlada, com motivo sanitizado e sem derrubar a API.

O SSN dedicado usa Docker/headless com Xvfb quando necessário, volume próprio em `data/runtime/socialstream`, sem acesso ao socket Docker, perfil/cookies do navegador, OBS config ou credenciais OBS. Consulte [docs/SOCIAL-STREAM-NINJA.md](docs/SOCIAL-STREAM-NINJA.md) para estado validado e limitações de teste real.

## Descoberta automática do YouTube live

A URL de live do YouTube deixou de ser informada manualmente por transmissão. O bloco público opcional `liveDiscovery` em `studioos.socialstream.json` habilita a descoberta do live corrente do canal, e a API gerencia a fonte SSN canônica `youtube-vid-<videoId>` de forma idempotente.

A descoberta lê somente a página pública `/streams` do próprio canal e identifica a live pelo badge público do YouTube. Não há Google Cloud, OAuth, API key, stream key nem scraping de busca, e o `youtubeAutoAdd` do SSN permanece desligado para que apenas o StudioOS seja dono da fonte por live. Um `manualLiveChatUrl` explícito tem precedência e desliga a descoberta automática. Fontes encerradas são apenas paradas, nunca excluídas. `GET /api/youtube/live-discovery` expõe somente leitura do estado atual. Consulte [docs/SOCIAL-STREAM-NINJA.md](docs/SOCIAL-STREAM-NINJA.md).

## APIs oficiais (avançado)

Integrações autenticadas continuam opcionais para envio de mensagens, moderação e ações/dados que exijam identidade. O arquivo externo `studioos.providers.json` preserva os IDs e a configuração pública já existentes. `enabled` mantém a disponibilidade geral do provider; `officialApiEnabled` deve ser explicitamente `true` para iniciar uma integração de API oficial. Ausente, esse campo é tratado como `false`. Segredos continuam em armazenamento seguro e não neste JSON.

Quando autorização for necessária, a experiência final deve ser `[ Connect ]` seguida do fluxo oficial. A autenticação Twitch/DPAPI permanece separada da captura simples por SSN. Consulte [docs/TWITCH.md](docs/TWITCH.md).

O caminho de áudio implementado é `LiveChat → InteractionDecisionPolicy → cooldown/anti-spam/anti-loop → Ollama → sanitizer → Piper → OBS Narration`, habilitado somente por configuração explícita; padrão e estado final são `AutoPlayInteractions=false`. O envio de texto ao chat não está implementado. Consulte `docs/AI-INTERACTIONS.md`.

## Twitch — API oficial avançada

O provider oficial Twitch/EventSub permanece preservado para modo avançado. Ele não é requisito de captura simples e só será iniciado quando `officialApiEnabled: true`; seu OAuth/Device Code Flow e o Secure Credential Helper não foram validados nesta subtask. Veja [docs/TWITCH.md](docs/TWITCH.md).

Os dados públicos de plataformas permanecem em `studioos.providers.json`, fora do repositório e montados read-only em `/app/config`. Twitch, YouTube e Kick seguem disponíveis nesse schema para integrações oficiais futuras; IDs/Client IDs existentes são preservados e não são exigidos pelo SSN simple capture. O API rejeita campos de segredo.

O helper Twitch continua uma dependência apenas do modo autenticado avançado; sua ativação depende de configuração de segurança própria e não faz parte da captura simples.

## Configuração externa de providers

O diretório local de configurações públicas é selecionado por `GFM_STUDIOOS_CONFIG_PATH` no `.env` ignorado. No ambiente atual, ele aponta para `D:\OBS-Live\.config`; `.env.example` contém somente `GFM_STUDIOOS_CONFIG_PATH=`. O Compose monta o diretório host somente para leitura em `/app/config`, onde a API lê `studioos.providers.json` no startup.

Fluxo: `.env` → `GFM_STUDIOOS_CONFIG_PATH` → mount read-only do Compose → `/app/config/studioos.providers.json` → carregador ASP.NET Core → opções/estado dos providers. A configuração é carregada no startup; alterações exigem reiniciar somente o serviço `api`.

O JSON aceita somente configuração pública: `enabled`, `officialApiEnabled`, `clientId`, `channel`, `channelId` e `broadcasterUserId`. `broadcasterUserId` Twitch e `channelId` YouTube podem ficar vazios para resolução futura. `enabled` sozinho não liga API oficial: somente `officialApiEnabled: true` ativa essa integração avançada. Os campos existentes de Twitch, YouTube e Kick são preservados. A captura simples usa as configurações separadas do SSN e não depende desses IDs.

Tokens de acesso/refresh, client secrets, authorization codes, senhas, cookies e credenciais privadas pertencem ao GFM StudioOS Secure Credential Helper/DPAPI, nunca ao JSON. Arquivo ausente ou configuração inválida deixa a API operacional e mantém os providers desabilitados, com motivo operacional sanitizado no log.

Autorização iniciada no StudioOS deverá mostrar o endereço oficial de verificação e o código de usuário; o usuário conclui a autorização no navegador. A sessão deve tratar refresh/revogação, EventSub reconnect/keepalive e desconexão. A mensagem normalizada segue pipeline, dedupe e buffer existentes. Interação automática global e autoplay da narração permanecem desligados; não há envio de mensagens Twitch nesta task. Estado atual e limitações: [docs/TWITCH.md](docs/TWITCH.md).

Endpoints de leitura: `GET /health`, `/api/obs/status`, `/api/obs/live-state`, `/api/obs/events`, `/api/chat/providers`, `/api/chat/state`, `/api/chat/messages`, `/api/chat/events`, `/api/interactions/state`, `/api/interactions/recent`, `/api/interactions/providers`, `/api/narration/state`, `/api/narration/recent` e `/api/youtube/live-discovery`.

## AI Interaction e TTS

O subsistema de interações processa eventos normalizados pelo mesmo pipeline de decisão, cooldown, contexto, AI, sanitização, TTS, notificações MediatR, buffer bounded e publisher. Mensagens comuns não recebem resposta automática: nesta etapa, respostas ocorrem somente por solicitação explícita do endpoint DEV ou pelo comando reservado `!studio`.

- `DevelopmentAiInteractionProvider` é determinístico, local e identificado pelo prefixo `[DEV AI]`. Ele **não é uma IA real**.
- `DevelopmentTextToSpeechProvider` continua disponível para testes e fallback; ele retorna metadados simulados e não gera áudio real.
- `OllamaAiInteractionProvider` usa IA real local; `DevelopmentAiInteractionProvider` permanece disponível para testes e fallback explícito.
- `PiperTextToSpeechProvider` gera WAV PCM real localmente com a voz `pt_BR-faber-medium` (português brasileiro, 22.050 Hz, mono, PCM 16-bit).
- **REAL LOCAL AI: YES. REAL LOCAL TTS: YES. REAL OBS NARRATION: YES.** Não há autoplay de conversas por padrão.
- Nenhuma API paga, banco, broker ou cache distribuído é necessário; o container Ollama local é a infraestrutura explícita da Task10.1.
- O endpoint `/api/interactions/dev/test` só é registrado quando `ASPNETCORE_ENVIRONMENT=Development`.

Exemplo de teste DEV:

```json
{
  "provider": "Development",
  "channelId": "local",
  "userId": "dev-user",
  "userDisplayName": "Developer",
  "message": "Olá StudioOS",
  "responseMode": "TextAndVoice"
}
```

Com `Interactions:TtsProvider=Piper`, `TextAndVoice` usa Ollama e Piper pelo pipeline normal e grava um artefato WAV em `data/runtime/tts/`. O provider e o resultado real/fallback são identificáveis nas APIs de interações. Consulte `docs/AI-INTERACTIONS.md` e `docs/TTS.md`.

## IA local

O provider selecionado é `Ollama`, executado no container `gfm-studioos-ollama`. A API usa `http://ollama:11434` pela rede Docker; a porta não é publicada no host. O modelo `qwen3:4b-instruct-2507-q4_K_M` fica no volume persistente `gfm-studioos-ollama-models` e é provisionado de forma idempotente pelo serviço one-shot `gfm-studioos-ollama-model`.

O Ollama para Windows não é necessário e deve permanecer parado no runtime normal. GPU NVIDIA é habilitada pelo `docker-compose.gpu.yml`; o Compose base oferece fallback CPU. Consulte [docs/LOCAL-AI-RUNTIME.md](docs/LOCAL-AI-RUNTIME.md) para topologia, operação, persistência e evidências da Task10.1.

Teste pelo StudioOS em ambiente `Development`:

```http
POST http://localhost:5080/api/interactions/dev/test
Content-Type: application/json

{
  "provider": "Development",
  "channelId": "local",
  "userId": "dev-user",
  "userDisplayName": "Developer",
  "message": "Responda em uma frase: qual é a sua função nesta live?",
  "responseMode": "Text"
}
```

Esse endpoint continua entrando no pipeline normal; com `Interactions:AiProvider=Ollama` e `Interactions:TtsProvider=Piper`, a resposta usa IA e voz locais reais. Ollama tem concorrência 1 e fila bounded; Piper tem concorrência 1, espera bounded, timeout e fallback Development explicitamente configurável. Falhas permanecem identificadas e não descartam o texto da IA. O fallback Development não é IA/áudio real.

## TTS local

- Engine: Piper 1.2.0, runtime Linux x86_64 usado pelo serviço API em Docker Compose; artefatos locais são montados em `/opt/tts-engine` somente para leitura.
- Voz: `pt_BR-faber-medium`, português brasileiro; saída WAV PCM, 22.050 Hz, mono, 16-bit.
- Para verificar: `GET /api/interactions/providers` (disponibilidade segura), e dentro do container `piper --version`; os artefatos ficam em `data/runtime/tts-engine/` e não são versionados.
- Smoke test completo: no ambiente Development, enviar `POST /api/interactions/dev/test` com `responseMode: TextAndVoice`; consultar `GET /api/interactions/recent` para o resultado e metadados do WAV. Isso não reproduz áudio.
- Áudios transitórios ficam em `data/runtime/tts/`; limpeza bounded: até 100 arquivos e retenção máxima de 60 minutos.
- Fallback para `DevelopmentTextToSpeechProvider` é habilitado explicitamente; quando usado, o resultado indica simulação e não deve ser tratado como áudio real.
- Limitação de distribuição: a licença de redistribuição do modelo de voz não está confirmada; não incluir a voz em distribuição do StudioOS sem análise jurídica/licenciamento independente. Detalhes e fontes em `docs/TTS.md`.

## Narração no OBS

A Task 08 adiciona `INarrationService` e um playback adapter sobre a conexão OBS WebSocket já existente. Piper produz WAV; somente artefatos registrados pelo pipeline dentro de `data/runtime/tts/` podem ser enfileirados. Como a API roda em container, o artefato é mapeado pelo GUID para o diretório host configurado em `Narration:HostRuntimeDirectory`; nenhum path arbitrário é aceito.

- Source dedicada: `GFM StudioOS - Narration`, tipo Media Source (`ffmpeg_source`), adicionada às cenas existentes sem recriá-las.
- Playback: FIFO, fila bounded de 5, uma reprodução por vez, sem interromper a atual; duração máxima de 15 s, timeout de início de 5 s e playback de 25 s.
- Roteamento verificado no profile `ETS`: somente Track 1 (track de live e `RecTracks=1` da gravação local configurada). Monitoring `Monitor Off` evita uma segunda rota de retorno/monitoramento.
- Volume inicial 70% e mute independentes da source. API: `PUT /api/narration/volume` (`{"volume":70}`) e `PUT /api/narration/mute` (`{"muted":false}`).
- `Narration:Enabled=true`; `Narration:AutoPlayInteractions=false`. Mensagens de chat não começam a falar automaticamente. O POST DEV gera frase controlada com Piper e usa a mesma fila/playback, sem aceitar caminho de arquivo.
- Para smoke test em Development: `POST /api/narration/dev/test` com `{"text":"Teste de narração do GFM StudioOS."}`; acompanhe `GET /api/narration/recent` até `Completed`. Isso não inicia live nem gravação.
- Estado `Queued`, `Preparing`, `Playing`, `Completed`/`Failed`, fila, volume, mute e tracks estão em `GET /api/narration/state`. `GET /health` expõe health separado de Narration.
- **Duas vozes (OBS-LIVE-BOT-10.2):** uma interação produz `Chat` (repetição determinística da mensagem, voz `pt_BR-jeff-medium`) e depois `Assistant` (resposta do modelo, voz `pt_BR-faber-medium`). São dois itens da mesma fila, nunca duas sources, então continuam tocando um após o outro sem sobreposição. A síntese do chat começa antes da chamada de IA, de modo que a mensagem do espectador pode ser falada enquanto a resposta ainda está sendo sintetizada. `GET /api/narration/state` expõe as vozes configuradas em `voices`; `GET /api/narration/recent` expõe `voiceRole`, `orderWithinInteraction` e `groupSequence` de cada item. A reprodução audível ainda **não** foi validada e `AutoPlayInteractions` permanece `false`.
- **REAL OBS NARRATION: YES. OBS AUDIO PLAYBACK: não toca no dispositivo local por monitoramento (Monitor Off); nenhuma transmissão pública é iniciada.**

Detalhes de lifecycle, segurança e limitações: `docs/NARRATION.md`.

## Resposta escrita no chat

A OBS-LIVE-BOT-11 implementa a escrita de texto no chat como capacidade própria, separada da captura e separada do áudio. Domain e Application conhecem apenas a fronteira `IChatResponseSender`; o Social Stream Ninja (SSN), HTTP e o DOM ficam só na Infrastructure, com client HTTP, options, DI e estado próprios. O sender real usa a única rota de escrita suportada pelo SSN 0.4.18, que não expõe comando de envio dedicado: `inspectSourcePage` → `interactSourcePage` fill → `interactSourcePage` pressKey Enter, com `confirm: true`.

- **Gate explícito:** `ChatResponses:Enabled=false` na configuração versionada. É o único valor mutável em runtime, o override vive apenas na memória e um restart volta ao valor configurado — não existe estado em disco capaz de carregar "habilitado" através de um reboot. `ChatResponses:AllowDevelopmentSender=false` recusa o sender `Development`, que nunca entrega nada e por isso só pode ser escolha explícita, nunca um fallback silencioso.
- **Anti-loop:** uma resposta escrita volta pela mesma captura de qualquer mensagem. O guardião de laço marca o eco na captura com `interaction.generatedByStudioOS`, que a política de decisão já trata como `SelfMessage`. A identidade do é a proteção primária e o texto é apenas o bootstrap do primeiro eco; a conta que entregou o primeiro eco é aprendida e, a partir daí, é reconhecida por identidade sozinha. O gatekeeper também recusa a própria conta do StudioOS, de forma independente do caminho de interação.
- **Idempotência:** chave `provider|channel|chatResponseId` em ledger bounded; a chave é reservada antes de qualquer escrita e liberada quando a escrita falha, para que um erro transitório da plataforma não bloqueie permanentemente uma resposta.
- **Isolamento de falha:** exceção, timeout, sender indisponível, fila cheia e falha de notificação nunca derrubam interação, áudio, narração, OBS ou captura. Nada no caminho automático lança exceção.
- **Fila bounded de leitor único:** executada fora do caminho da interação, com teto por tentativa (`CommandTimeoutSeconds`). Quando a fila está cheia a resposta mais nova é recusada e o motivo é distinguido de writer parado.
- **Retentativa fail-closed:** só condições comprovadamente anteriores a qualquer digitação são repetidas (`STALE_PAGE_REF`, página indisponível, janela indisponível). Depois que o fill é aceito, StudioOS não consegue saber se o Enter chegou à página, então a sequência nunca se repete: no máximo uma resposta é perdida e nenhuma é publicada duas vezes.
- **Sem banco, EF Core, Redis, broker, cloud ou container novo.** Estado em memória bounded; o ledger não é persistido e um restart simplesmente recomeça limpo.

Endpoints read-only de estado, histórico, senders e capacidades, mais `PUT /api/chat-responses/settings` e o endpoint de desenvolvimento `POST /api/chat-responses/dev/send` (só em Development, e ainda obedecendo o gate). `GET /health` expõe `chatResponses`; desligado é o estado saudável, e `Degraded` fica reservado para "habilitado porém incapaz de escrever". `GET /api/chat-responses/providers` reporta leitura e escrita separadas por plataforma, sem nunca inferir uma da outra.

**O que não foi validado:** nenhuma mensagem foi escrita em uma live real. A entrega foi validada de ponta a ponta com o processo no ar e o gate desligado; ligar o gate para uma transmissão real é uma decisão do analista, não uma etapa de validação pendente.

Detalhes de configuração, ordem dos gates e limitações: `docs/CHAT-RESPONSES.md`.

## Arquitetura
C#/.NET 10 é o núcleo (ASP.NET Core, Vertical Slice, CQRS, MediatR oficial, Ardalis.Result, FluentValidation, Serilog, OpenAPI, Domain Events e Mapping). Python é especializado em IA/mídia. C++ é opcional para nativo/performance. n8n é orquestrador local.

O projeto é local-first e busca custo de infraestrutura zero. EF Core fica disponível quando uma feature realmente exigir persistência, sem provider obrigatório; estado e buffers em memória são preferidos enquanto forem suficientes. Não há Redis, RabbitMQ, Kafka, MongoDB ou banco obrigatório.

Segredos locais ficam somente no `.env` ignorado. O n8n exige `N8N_ENCRYPTION_KEY`, `N8N_HMAC_SIGNATURE_SECRET`, `N8N_BINARY_DATA_SIGNING_SECRET` e `N8N_USER_MANAGEMENT_JWT_SECRET`; seu estado em `data/n8n` também é local, sensível e não versionado.

Leia `PROJECT.md`, `PROJECT-STATE.md`, `AI-WORKFLOW.md` e `docs/ARCHITECTURE.md`.

## Histórico Git sanitizado

O histórico local e a branch remota ativa foram reescritos após a rotação dos segredos do n8n. Clones anteriores à sanitização devem ser substituídos por um clone novo. Para preservar trabalho local legítimo, reaplique somente os commits necessários sobre o clone limpo; não faça merge do histórico antigo.

## Módulos
- Live Engine / AI Content Studio
- Content Engine
- Live Command Center
- Configuration, Backup & Restore

O módulo de backup prevê snapshot integral de `C:\Users\gfmau\AppData\Roaming\obs-studio` e Job automático após o fechamento do OBS.
