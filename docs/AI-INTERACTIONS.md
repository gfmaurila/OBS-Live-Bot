# AI Interaction e Ollama local

## Escopo das OBS-LIVE-BOT-05 e OBS-LIVE-BOT-06

A Task 05 implementou a foundation local e testável para transformar `LiveChatEvent` normalizado em uma decisão `Ignore` ou `Respond`. A Task 06 integrou o Ollama como IA real local sem acoplar Application ao runtime. Não há chatbot real de plataforma, STT, memória persistente, RAG, automação OBS ou Content Engine.

## Implementado

- `IInteractionOrchestrator` coordena decision policy, cooldown, context builder, AI, sanitizer, TTS opcional, notificações, buffer e publisher.
- `IInteractionDecisionPolicy` é determinística e não chama IA para decidir.
- Mensagens vazias, eventos não-message, provider desconhecido, identidade ausente, conteúdo excessivo, bots e identidade própria são ignorados.
- Mensagens comuns são ignoradas. O comando reservado `!studio` responde com o modo padrão; o endpoint DEV pode solicitar `Text`, `Voice` ou `TextAndVoice` explicitamente.
- Identidade própria é provider-scoped (`Provider:UserId`); IDs iguais em plataformas diferentes não são inferidos como a mesma identidade.
- `InteractionCooldownTracker` é thread-safe, bounded e configurável para escopo User, Channel ou Global. O padrão é `Provider + Channel + User`.
- `InteractionBuffer` é thread-safe, bounded, usa oldest eviction, mantém sequence monotônica e não persiste dados.
- `InteractionDecidedNotification`, `InteractionCompletedNotification` e `InteractionFailedNotification` usam MediatR oficial.
- `NoOpInteractionEventPublisher` preserva o ponto de extensão sem depender de n8n, UI ou OBS.

## AI e contexto

`IAiInteractionProvider` não depende de SDK específico. O provider selecionado é `Ollama`; o adapter HTTP e seus detalhes permanecem em Infrastructure. O `DevelopmentAiInteractionProvider`, determinístico e marcado com `[DEV AI]`, continua registrado para testes e fallback configurável e não representa IA real.

O modelo local principal é `qwen3:4b-instruct-2507-q4_K_M` (~2,5 GB), executado pelo Ollama 0.34.4 no Windows host. A escolha privilegia português, modo instruct sem raciocínio exposto, baixa latência e folga na RTX 3060 de 12 GB. O runtime não usa API key nem envia conteúdo a um serviço cloud.

`IInteractionContextBuilder` mantém separados:

- instruções do sistema;
- mensagem não confiável do usuário;
- contexto bounded de respostas recentes;
- identidade/correlation da interação.

Texto do chat como “ignore suas instruções anteriores” permanece `UserMessage` e não modifica estruturalmente `SystemInstructions`. Essa separação é uma foundation de segurança, não uma solução completa para prompt injection.

`IAiResponseSanitizer` rejeita resposta vazia, remove controles inválidos, preserva Unicode/acentuação/emoji e limita a saída por Unicode scalar conforme `MaxResponseCharacters`.

## Configuração

A seção `Interactions` controla enablement, capacidades, limite de resposta, modo padrão, cooldown, providers, contexto, comando reservado, idioma, voz e identidades próprias. Não contém secrets.

Defaults principais:

- buffer: 100;
- cooldown state: 1000;
- cooldown: 5 segundos por usuário provider-scoped;
- AI provider: Ollama;
- TTS provider: Development;
- resposta máxima: 500 caracteres.

Configuração Ollama padrão:

- URL host: `http://localhost:11434` (`host.docker.internal` para a API em container);
- timeout: 45 segundos;
- temperatura: 0,2;
- saída: até 160 tokens;
- concorrência: 1 inferência;
- fila: até 2 requisições, com espera máxima de 2 segundos;
- fallback Development: habilitado explicitamente.

O provider propaga `CancellationToken`, aplica timeout próprio e retorna erros controlados para timeout, HTTP, resposta inválida/vazia, indisponibilidade e overload. Métricas locais incluem requests, successes, failures, timeouts, busy rejections, duração média e timestamps de sucesso/falha. A fila e a concorrência são bounded; não existe broker ou fila infinita.

## API

- `GET /api/interactions/state`
- `GET /api/interactions/recent?limit=20`
- `GET /api/interactions/providers`
- `POST /api/interactions/dev/test` somente em `Development`

O POST DEV cria um evento sintético e entra pelos mesmos command, validation e orchestrator usados pelo pipeline de aplicação. Payload inválido retorna HTTP 400.

## Failure isolation

Falhas e exceptions de AI ou TTS viram resultados explícitos no buffer. Quando habilitado, o fallback DEV registra `AiFallbackUsed` e `PrimaryAiErrorCode`, preservando a visibilidade da falha primária. Health permanece `Degraded` se o Ollama selecionado estiver indisponível, mesmo que a API, OBS, Chat e n8n continuem operacionais. Falha do publisher ocorre depois do buffering e não remove o resultado.

## Futuro, não implementado

- outros runtimes locais ou provider externo opcional;
- provider externo opcional;
- Windows TTS, Piper ou engine real;
- envio de texto para chat;
- reprodução de áudio na live;
- OBS actions, moderação e eventos de monetização;
- memória persistente, RAG ou vector database;
- publicação real para n8n, UI, SignalR ou OBS.
