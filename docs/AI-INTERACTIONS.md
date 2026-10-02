# AI Interaction, Ollama e TTS local

## Escopo implementado (Tasks 05–10)

A foundation recebe eventos normalizados, toma decisão determinística, aplica prevenção de loop/cooldown, monta contexto bounded e executa AI, sanitização, TTS opcional, notificações MediatR e buffer. A Task 08 acrescentou narração OBS isolada; a Task10 integrou um trigger de chat real a esse pipeline de áudio. Autoplay permanece desligado por padrão e após a validação. Não há STT, memória persistente, RAG ou Content Engine.

Mensagens comuns são ignoradas por padrão. `!studio` e solicitações do endpoint DEV podem responder. Identidade própria é provider-scoped. O buffer guarda até 100 resultados, oldest eviction e sequência monotônica. O publisher inicial é NoOp.

## IA local

`IAiInteractionProvider` seleciona Ollama em Infrastructure; Application não conhece protocolo HTTP. O modelo principal é `qwen3:4b-instruct-2507-q4_K_M` (~2,5 GB), executado pelo Ollama local. Não há API key nem envio a cloud. `DevelopmentAiInteractionProvider` permanece para testes e fallback explícito, marcado `[DEV AI]`.

System instructions, contexto e mensagem não confiável do chat permanecem campos/roles separados. O sanitizer de resposta rejeita conteúdo vazio, remove controles inválidos, preserva Unicode e limita caracteres.

Ollama usa timeout configurável, cancelamento propagado, concorrência 1 e fila bounded (2 solicitações, espera máxima padrão 2 s). Falhas/overload são controlados; fallback DEV, se habilitado, fica identificado e não mascara indisponibilidade do provider primário no health.

## TTS local

O provider selecionado é `PiperTextToSpeechProvider`, com voz `pt_BR-faber-medium`, português brasileiro, saída WAV PCM 22.050 Hz, mono, 16-bit. A arquitetura, execução, licença e smoke real estão em [TTS.md](TTS.md). `DevelopmentTextToSpeechProvider` continua registrado para testes e fallback, mas só produz metadados simulados.

Piper tem timeout de 20 s, concorrência 1, fila bounded de 2 solicitações e espera máxima de 2 s. Artefatos transitórios usam `data/runtime/tts/`, GUID no nome e cleanup limitado a 100 arquivos/60 minutos. `AllowDevelopmentFallback` é explícito. Se TTS falhar, o texto AI permanece no resultado; fallback e simulação são indicados separadamente, nunca como áudio real.

TTS concluído e OBS playback concluído são estados distintos. Se `Narration:AutoPlayInteractions=true` for explicitamente habilitado, um `InteractionCompletedNotification` pode enfileirar o artefato para `INarrationService`; default atual `false`. A API de narração Development também sintetiza com o provider real e usa a mesma fila, sem aceitar paths. Consulte [NARRATION.md](NARRATION.md) para routing/estados.

## API

- `GET /api/interactions/state`
- `GET /api/interactions/recent?limit=20`
- `GET /api/interactions/providers`
- `POST /api/interactions/dev/test` somente em `Development`

Narração/playback:

- `GET /api/narration/state`
- `GET /api/narration/recent`
- `POST /api/narration/dev/test` somente em `Development`
- `PUT /api/narration/mute`
- `PUT /api/narration/volume`

O endpoint de interação POST usa o mesmo command/validation/orchestrator do pipeline. Com `responseMode: TextAndVoice`, a configuração pode exercitar Ollama + Piper reais e gerar um artefato. Com a flag padrão desligada, gerar WAV não inicia playback OBS.

## Estado

**REAL LOCAL AI: YES. REAL LOCAL TTS: YES. REAL OBS NARRATION: YES.** `Narration:AutoPlayInteractions=false`; nenhuma transmissão pública é iniciada pelo serviço. Task10 validou o pipeline automático de áudio em uma mensagem YouTube real; isso não representa E2E de IA/áudio em Twitch/Kick nem envio de texto às plataformas.

## Task10 — áudio automático controlado

Task10 conectou o evento normalizado de chat ao orquestrador existente. Uma interação elegível requer `AutoPlayInteractions=true` explicitamente e um trigger (comando configurável `!studio` ou menção reconhecida); mensagens comuns recebem `NoTrigger`. Self/bot/generated events, duplicatas, mensagens inválidas/oversized e eventos dentro dos cooldowns são rejeitados antes de executar AI/TTS. Cooldowns global e por identidade provider-scoped são conservadores; filas e concorrência de Ollama, Piper e Narration continuam bounded, com falha isolada do LiveChat. Nunca se infere identidade entre plataformas.

Foi validada uma mensagem REAL do YouTube na source SSN `live_chat` até Ollama local, sanitizer, Piper/WAV e OBS; o analista confirmou ter ouvido o áudio. Não houve resposta escrita no chat. `AutoPlayInteractions` foi desligado após o teste e seu estado final é `false`. A validação real de IA/áudio descrita aqui é YouTube; não implica E2E AI/TTS em Twitch ou Kick.

## Task10.1 — Ollama gerenciado por Docker

O runtime oficial do provider local agora é `gfm-studioos-ollama`, acessado pela API em `http://ollama:11434`. O modelo permanece fora da imagem e do Git no volume `gfm-studioos-ollama-models`; o serviço one-shot `gfm-studioos-ollama-model` provisiona somente quando ausente. A API não depende da prontidão do Ollama para iniciar: indisponibilidade degrada Interactions sem derrubar Chat, OBS ou os demais endpoints.

A validação com o Ollama do Windows parado comprovou inferência real via API, sem fallback, e uso `100% GPU` reportado pelo Ollama na RTX 3060. Restart/recriação preservaram o modelo e o segundo provisionamento não baixou novamente. O Compose base mantém fallback CPU; o override `docker-compose.gpu.yml` solicita GPU NVIDIA. Detalhes em [LOCAL-AI-RUNTIME.md](LOCAL-AI-RUNTIME.md).

O E2E de live foi repetido no runtime Docker e concluído: mensagens reais `!studio` na source SSN `live_chat` de uma transmissão ao vivo do YouTube geraram resposta real pelo Ollama Docker, TTS Piper real e narração concluída no OBS, e o analista confirmou explicitamente ter ouvido o áudio. Nenhuma dependência do Ollama do Windows, que permaneceu parado. `AutoPlayInteractions` foi desligado após o teste e seu estado final é `false`.

Semântica: `Interaction.Completed` significa que a geração da resposta (AI + TTS) concluiu com sucesso e foi publicada. A reprodução OBS é um lifecycle separado da Narration (`Queued → Started → Completed/Failed`), correlacionado, porém sem acoplamento de domínio; portanto a interação pode aparecer Completed antes do fim do áudio.

## OBS-LIVE-BOT-10.1.2 — limite de automação preservado

A descoberta automática do YouTube live alterou apenas qual fonte SSN está ativa, não o pipeline de interação. Nenhuma mensagem real de chat pode responder por voz ou texto enquanto `AutoPlayInteractions=false`, independentemente de como a fonte foi criada.

Validação com uma mensagem real do YouTube recebida na source descoberta automaticamente: a decisão foi `Ignore` e a interação terminou com `responseText`, provider/modelo de IA, provider de TTS e `audioPath` nulos. A narração reportou `started=0`, `completed=0` e `lastPlaybackAtUtc=null`, confirmando que nenhum áudio foi gerado nem reproduzido. `AutoPlayInteractions` permaneceu `false` e o trigger `!studio` não foi acionado.

Um detalhe de configuração merece registro: a mensagem de validação continha a palavra `StudioOS`, que faz parte de `BotMentionTriggers`. Por isso ela foi classificada como menção, ou seja, `AutoPlayDisabled` em vez de `NoTrigger`. O efeito é o mesmo e igualmente correto:ignorada sem executar IA/TTS. Mensagens comuns sem menção e sem comando continuam recebendo `NoTrigger`, como observado em outros eventos reais do mesmo source.

## OBS-LIVE-BOT-10.2 — duas vozes por interação

Uma interação elegível agora produz dois clipes de voz em vez de um, com papéis semânticos: `Chat` repete de forma determinística a mensagem aceita do chat e `Assistant` fala a resposta gerada. As vozes são `pt_BR-jeff-medium` e `pt_BR-faber-medium`; a atribuição vem da configuração, nunca do nome do arquivo do modelo.

Os dois ramos rodam em paralelo. O texto do chat é construído e a sua síntese é iniciada **antes** da chamada de IA, porque não depende do modelo, e o grupo é entregue ao narrador com a síntese da resposta ainda em andamento. Consequentemente o clipe do chat já pode estar sendo reproduzido enquanto a resposta ainda está sendo sintetizada. O resultado da interação passa a expor `chatSpeechText`, `chatTtsVoice`, `chatTtsSuccess`, `chatAudioPath` e `chatArtifactId`, além de `InteractionAudioLatency` com os tempos separados de `chatTts`, `ai`, `assistantTts`, o início de cada ramo após o aceite e o total.

O texto falado do chat é determinístico: o trigger é removido com a mesma regra usada na verificação do trigger, o prefixo `"{username} disse: {message}"` é opcional, URLs viram `"link"`, espaços e caracteres de controle são normalizados, a pontuação é colapsada, o corpo tem tamanho limitado e texto vazio é rejeitado. Nenhum segredo, token ou URL bruta chega à voz.

Isolamento de falha é bidirecional: falha na síntese do chat não impede a fala da resposta, e falha da IA, resposta rejeitada ou resposta que não pôde ser voiceada não impedem a fala da mensagem. Uma falha do narrador não altera o status da interação e não trava os grupos atrás. O narrador nunca bloqueia a interação: ele é posterior à resposta.

Ordem de reprodução: `Chat` antes de `Assistant` dentro da mesma interação, e os grupos são admitidos em ordem crescente, o que produz `A chat, A assistant, B chat, B assistant`. A chave de ordenação é o momento em que o texto da resposta ficou disponível, portanto uma resposta lenta é falada depois de uma resposta mais rápida que chegou depois, mas o clipe de chat dessa interação não é atrasado por ela.

A chave de ordenação e o papel de cada item ficam visíveis em `GET /api/narration/recent` (`voiceRole`, `orderWithinInteraction`, `groupSequence`) e as vozes configuradas em `GET /api/narration/state` (`voices`). Detalhes de fila, routing e configuração em [NARRATION.md](NARRATION.md); os dois modelos e suas licenças em [TTS.md](TTS.md).

Estado: `Narration:AutoPlayInteractions=false` e nenhuma reprodução audível foi realizada nesta fase. Nenhuma configuração do OBS foi alterada e o Ollama do Windows permaneceu parado; a síntese do chat não depende do modelo local.
