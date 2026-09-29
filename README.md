# OBS Live Bot / Live Command Center

Plataforma local para automação de live no OBS, IA/TTS opcional, Content Engine, Command Center Windows e backup/restore integral do ambiente OBS.

## Estado
`OBS-LIVE-BOT-00` a `OBS-LIVE-BOT-07` estão concluídas. A Task 08 está em validação: playback local controlado pelo OBS foi implementado; Autoplay de interações permanece desativado.

## Serviços locais

| Serviço | URL |
|---|---|
| OBS Live Bot API | `http://localhost:5080` |
| OBS Live Bot Swagger | `http://localhost:5080/swagger` |
| OBS Live Bot Swagger UI | `http://localhost:5080/swagger/index.html` |
| OBS Live Bot n8n | `http://localhost:5679` |
| GFM StudioOS Ollama | `http://localhost:11434` |

## n8n — Desenvolvimento

URL: `http://localhost:5679`

### Owner local de desenvolvimento

Email: `dev@gfmstudio.local`

Senha: `GfmStudioOS@Dev2026`

> **ATENÇÃO:** esta é uma credencial conhecida destinada exclusivamente ao ambiente LOCAL de desenvolvimento do GFM StudioOS. Nunca reutilizar esta senha em produção ou em uma instância exposta externamente.

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
| Interactions | GET | `/api/interactions/state` | `http://localhost:5080/api/interactions/state` |
| Interactions | GET | `/api/interactions/recent` | `http://localhost:5080/api/interactions/recent` |
| Interactions | GET | `/api/interactions/providers` | `http://localhost:5080/api/interactions/providers` |
| Interactions (Development) | POST | `/api/interactions/dev/test` | `http://localhost:5080/api/interactions/dev/test` |
| Narration | GET | `/api/narration/state` | `http://localhost:5080/api/narration/state` |
| Narration | GET | `/api/narration/recent` | `http://localhost:5080/api/narration/recent` |
| Narration (Development) | POST | `/api/narration/dev/test` | `http://localhost:5080/api/narration/dev/test` |
| Narration | PUT | `/api/narration/mute` | `http://localhost:5080/api/narration/mute` |
| Narration | PUT | `/api/narration/volume` | `http://localhost:5080/api/narration/volume` |

Endpoints de leitura: `GET /health`, `/api/obs/status`, `/api/obs/live-state`, `/api/obs/events`, `/api/chat/providers`, `/api/chat/state`, `/api/chat/messages`, `/api/chat/events`, `/api/interactions/state`, `/api/interactions/recent`, `/api/interactions/providers`, `/api/narration/state` e `/api/narration/recent`.

## AI Interaction e TTS

O subsistema de interações processa eventos normalizados pelo mesmo pipeline de decisão, cooldown, contexto, AI, sanitização, TTS, notificações MediatR, buffer bounded e publisher. Mensagens comuns não recebem resposta automática: nesta etapa, respostas ocorrem somente por solicitação explícita do endpoint DEV ou pelo comando reservado `!studio`.

- `DevelopmentAiInteractionProvider` é determinístico, local e identificado pelo prefixo `[DEV AI]`. Ele **não é uma IA real**.
- `DevelopmentTextToSpeechProvider` continua disponível para testes e fallback; ele retorna metadados simulados e não gera áudio real.
- `OllamaAiInteractionProvider` usa IA real local; `DevelopmentAiInteractionProvider` permanece disponível para testes e fallback explícito.
- `PiperTextToSpeechProvider` gera WAV PCM real localmente com a voz `pt_BR-faber-medium` (português brasileiro, 22.050 Hz, mono, PCM 16-bit).
- **REAL LOCAL AI: YES. REAL LOCAL TTS: YES.** O playback de narração controlado no OBS está sendo validado na Task 08; não há autoplay de conversas por padrão.
- Nenhuma API paga, banco, broker, cache distribuído ou novo container é necessário.
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

O provider selecionado é `Ollama`, executado no Windows host e acessível localmente em `http://localhost:11434`. O modelo principal é `qwen3:4b-instruct-2507-q4_K_M` (aproximadamente 2,5 GB), escolhido como modelo instruct multilíngue compacto para baixa latência no hardware local. A API em container acessa o mesmo runtime por `host.docker.internal`; o Ollama não faz parte do container n8n.

Verificação do runtime e do modelo:

```powershell
ollama --version
ollama list
curl.exe http://localhost:11434/api/version
```

Se o runtime não estiver ativo, inicie o aplicativo Ollama para Windows ou execute `ollama serve`. Não exponha a porta 11434 publicamente. O status seguro do provider está disponível em `GET /api/interactions/providers` e o estado selecionado em `GET /api/interactions/state`.

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
- **REAL OBS NARRATION: YES. OBS AUDIO PLAYBACK: não toca no dispositivo local por monitoramento (Monitor Off); nenhuma transmissão pública é iniciada.**

Detalhes de lifecycle, segurança e limitações: `docs/NARRATION.md`.

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
