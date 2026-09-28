# AI Interaction Foundation

## Escopo da OBS-LIVE-BOT-05

Esta task implementa uma foundation local e testável para transformar `LiveChatEvent` normalizado em uma decisão `Ignore` ou `Respond`. Não implementa chatbot real de plataforma, STT, memória persistente, RAG, automação OBS ou Content Engine.

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

`IAiInteractionProvider` não depende de SDK específico. O provider selecionado é `Development`, que retorna uma resposta determinística marcada com `[DEV AI]`. Ele não é uma IA real e não requer chave, Internet ou runtime de LLM.

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
- AI provider: Development;
- TTS provider: Development;
- resposta máxima: 500 caracteres.

## API

- `GET /api/interactions/state`
- `GET /api/interactions/recent?limit=20`
- `GET /api/interactions/providers`
- `POST /api/interactions/dev/test` somente em `Development`

O POST DEV cria um evento sintético e entra pelos mesmos command, validation e orchestrator usados pelo pipeline de aplicação. Payload inválido retorna HTTP 400.

## Failure isolation

Falhas e exceptions de AI ou TTS viram resultados `Failed` explícitos no buffer. Falha do publisher ocorre depois do buffering e é registrada sem remover o resultado. Nenhuma dessas falhas deve encerrar OBS, Chat, API ou n8n.

## Futuro, não implementado

- Ollama ou outro runtime local;
- provider externo opcional;
- Windows TTS, Piper ou engine real;
- envio de texto para chat;
- reprodução de áudio na live;
- OBS actions, moderação e eventos de monetização;
- memória persistente, RAG ou vector database;
- publicação real para n8n, UI, SignalR ou OBS.
