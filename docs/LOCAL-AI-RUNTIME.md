# Runtime local de IA

## Topologia oficial

O runtime local de IA do GFM StudioOS é o Ollama gerenciado pelo Docker Compose. A API usa a abstração `IAiInteractionProvider`; somente Infrastructure conhece o endpoint configurável. No ambiente Compose, o endpoint é `http://ollama:11434` pela rede Docker `obs-live-bot`.

```text
StudioOS API -> http://ollama:11434 -> gfm-studioos-ollama -> Qwen
```

O Ollama não publica a porta 11434 no host. O runtime cloud do Ollama fica desabilitado por `OLLAMA_NO_CLOUD=true`. Depois do provisionamento inicial, inferência não exige Internet.

## Serviços e armazenamento

- Runtime: `gfm-studioos-ollama`.
- Provisionador one-shot: `gfm-studioos-ollama-model`.
- Modelo: `qwen3:4b-instruct-2507-q4_K_M`.
- Volume persistente: `gfm-studioos-ollama-models`, montado em `/root/.ollama`.
- Restart policy do runtime: `unless-stopped`.
- Readiness: `ollama list` no healthcheck.

O provisionador espera readiness com limite, consulta o modelo com `ollama show` e só executa `ollama pull` quando necessário. Pulls com falha têm tentativas limitadas e aproveitam o comportamento resumível do Ollama. O provisionador terminar com falha não impede a API ou os outros serviços de iniciar; Interactions fica `Degraded/Unavailable` até a recuperação.

O volume não é removido por restart ou recriação normal de container. Não use `docker compose down --volumes` se a intenção for preservar os modelos.

## Desenvolvimento com GPU NVIDIA

Em máquinas com Docker/NVIDIA funcional, use o override que solicita as GPUs ao Docker:

```powershell
docker compose -f docker-compose.yml -f docker-compose.gpu.yml up -d
```

Validação real da Task10.1 na RTX 3060 12 GB:

- GPU visível dentro do container: `NVIDIA GeForce RTX 3060`;
- Ollama selecionou a biblioteca CUDA;
- modelo carregado e inferência real concluída;
- `ollama ps` reportou `100% GPU` para o Qwen.
- o Compose base também foi validado com inferência real e `ollama ps` reportando `100% CPU`.

Se o Docker não puder disponibilizar GPU, use o Compose base como fallback CPU:

```powershell
docker compose up -d
```

Esse fallback mantém a mesma API, rede, modelo e volume, com desempenho inferior. O instalador futuro deverá detectar e gerenciar essa escolha; a Task10.1 não implementa o instalador completo.

## Verificação operacional

```powershell
docker compose ps --all
docker exec gfm-studioos-ollama ollama list
curl.exe http://localhost:5080/api/interactions/providers
```

O estado esperado da API é `Ollama / Ready`, com o modelo configurado. `GET /health` pode continuar globalmente `degraded` quando OBS ou Narration estiverem indisponíveis; isso não significa falha do Ollama se `interactions` estiver `healthy`.

O Ollama para Windows não é necessário para o caminho Compose e pode permanecer parado. A Task10.1 não o desinstala. Não exponha a API do Ollama publicamente e não armazene modelos no Git.

## Evidência de validação da Task10.1

- Windows Ollama parado e `localhost:11434` sem listener/resposta.
- API recriada usando `http://ollama:11434`.
- inferência real via API retornou o modelo Qwen, `aiSuccess=true` e `aiFallbackUsed=false`.
- restart e recriação do container preservaram o mesmo modelo no volume.
- segundo provisionamento retornou `action=existing`, sem redownload.
- parada controlada do Ollama degradou somente Interactions; API, Chat e endpoints OBS continuaram respondendo.
- recuperação restaurou o provider para `Ready`.
- `AutoPlayInteractions=false`; nenhum TTS/playback/live foi iniciado durante a validação de infraestrutura.

## Evidência de validação da Task10.1.1 — E2E final de live

- Ollama do Windows parado durante todo o E2E e `localhost:11434` sem listener.
- Transmissão real do YouTube como fonte de entrada, via SSN `/live_chat`.
- Duas mensagens reais `!studio` capturadas, normalizadas e decididas como `Respond`.
- Geração real pelo Ollama Docker; nenhum fallback e nenhuma chamada ao host.
- Piper real: dois WAV PCM16 mono 22050 Hz válidos e não silenciosos.
- Narração: `queued=2, started=2, completed=2, failed=0`, playback concluído na source `GFM StudioOS - Narration`.
- **O analista confirmou explicitamente ter ouvido as respostas de áudio.**
- `AutoPlayInteractions=false` ao final; nenhum restart, streaming ou gravação alterado.

Conclusão: o E2E YouTube -> SSN -> StudioOS -> Ollama Docker -> Piper -> OBS está **PASS** e audível. O runtime local de IA oficial é exclusivamente o container; o Ollama do Windows permanece parado e não desinstalado. Descoberta automática de live, resposta escrita no chat e voz do chat lida a partir da mensagem continuam não implementadas.
