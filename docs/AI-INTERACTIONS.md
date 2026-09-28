# AI Interaction, Ollama e TTS local

## Escopo implementado (Tasks 05–07)

A foundation recebe eventos normalizados, toma decisão determinística, aplica prevenção de loop/cooldown, monta contexto bounded e executa AI, sanitização, TTS opcional, notificações MediatR, buffer e publisher. Não há integração real de chat de plataforma, STT, memória persistente, RAG, automação/playback OBS ou Content Engine.

Mensagens comuns são ignoradas por padrão. `!studio` e solicitações do endpoint DEV podem responder. Identidade própria é provider-scoped. O buffer guarda até 100 resultados, oldest eviction e sequência monotônica. O publisher inicial é NoOp.

## IA local

`IAiInteractionProvider` seleciona Ollama em Infrastructure; Application não conhece protocolo HTTP. O modelo principal é `qwen3:4b-instruct-2507-q4_K_M` (~2,5 GB), executado pelo Ollama local. Não há API key nem envio a cloud. `DevelopmentAiInteractionProvider` permanece para testes e fallback explícito, marcado `[DEV AI]`.

System instructions, contexto e mensagem não confiável do chat permanecem campos/roles separados. O sanitizer de resposta rejeita conteúdo vazio, remove controles inválidos, preserva Unicode e limita caracteres.

Ollama usa timeout configurável, cancelamento propagado, concorrência 1 e fila bounded (2 solicitações, espera máxima padrão 2 s). Falhas/overload são controlados; fallback DEV, se habilitado, fica identificado e não mascara indisponibilidade do provider primário no health.

## TTS local

O provider selecionado é `PiperTextToSpeechProvider`, com voz `pt_BR-faber-medium`, português brasileiro, saída WAV PCM 22.050 Hz, mono, 16-bit. A arquitetura, execução, licença e smoke real estão em [TTS.md](TTS.md). `DevelopmentTextToSpeechProvider` continua registrado para testes e fallback, mas só produz metadados simulados.

Piper tem timeout de 20 s, concorrência 1, fila bounded de 2 solicitações e espera máxima de 2 s. Artefatos transitórios usam `data/runtime/tts/`, GUID no nome e cleanup limitado a 100 arquivos/60 minutos. `AllowDevelopmentFallback` é explícito. Se TTS falhar, o texto AI permanece no resultado; fallback e simulação são indicados separadamente, nunca como áudio real.

## API

- `GET /api/interactions/state`
- `GET /api/interactions/recent?limit=20`
- `GET /api/interactions/providers`
- `POST /api/interactions/dev/test` somente em `Development`

O endpoint POST usa o mesmo command/validation/orchestrator do pipeline. Com `responseMode: TextAndVoice`, a configuração atual pode exercitar Ollama + Piper reais; a execução gera artefato, sem reprodução automática.

## Estado

**REAL LOCAL AI: YES. REAL LOCAL TTS: YES. OBS AUDIO PLAYBACK: NOT YET.** Não existe chatbot real de Twitch/YouTube/TikTok nesta foundation; OBS narration/audio routing pertence à Task 08.
