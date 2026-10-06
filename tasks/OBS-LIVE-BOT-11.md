# TASK: OBS-LIVE-BOT-11 — Written Chat Responses (IChatResponseSender boundary)

STATUS: PASS (entregue e desligado; nenhuma escrita em live real)

## Contexto

`docs/ARCHITECTURE.md` e `docs/SOCIAL-STREAM-NINJA.md` declaram que
`IChatResponseSender` é apenas uma fronteira conceitual e que respostas escritas no chat
permanecem não implementadas. As tasks 09.3, 10, 10.1.x e 10.2 construíram apenas o caminho de
captura (SSE, leitura) e o caminho de áudio (Ollama -> Piper -> fila -> OBS).

O envio de texto para a plataforma nunca foi implementado, e é justamente o lado que pode gerar
loop: uma resposta escrita volta para a captura como mensagem de chat.

## Objetivo

Implementar a escrita de resposta no chat como capacidade independente, atrás de uma fronteira
`IChatResponseSender`, com separação estrita entre captura e escrita.

## Escopo

- Fronteira `IChatResponseSender` no Application com implementações por plataforma em Infrastructure.
- Sender real sobre a API de comandos suportada do Social Stream Ninja 0.4.18
  (`inspectSourcePage` -> `interactSourcePage` fill -> `interactSourcePage` pressKey Enter).
  O SSN 0.4.18 não expõe comando de envio de mensagem dedicated; a interação de página é a única
  rota de escrita suportada e ela é usada como está, com `confirm: true`.
- Sender `Development` determinístico, selecionável explicitamente, sem entrega externa.
- Fila bounded de escrita com reader único, executada fora do caminho da interação.
- Gate explícito `ChatResponses:Enabled` com default `false`.
- Anti-loop: ledger bounded de textos escritos marca o eco na captura via
  `interaction.generatedByStudioOS`, que a política de decisão já trata como `SelfMessage`;
  supressão de texto duplicado na janela de eco.
- Idempotência: chave por `provider|channel|interactionId` em ledger bounded.
- Isolamento de falha: exceção, timeout, sender indisponível, fila cheia e falha de notificação
  nunca derrubam interação, áudio, narração, OBS ou captura.
- APIs read-only de estado/histórico/senders e endpoint de desenvolvimento para envio controlado.
- Sem banco, sem EF Core, sem Redis/broker/cloud e sem novo container.

## Fora de escopo

- OAuth, stream key, cookie, API key e qualquer segredo novo.
- Envio por API oficial de plataforma (Twitch Helix, YouTube Data API, Kick).
- Alteração de `OBS_CONFIG_ROOT`, fontes, cenas, áudio ou settings do OBS.
- Alterar o runtime Docker do Ollama (`gfm-studioos-ollama`), o Compose ou seus volumes.
- Reprodução audível de áudio ou envio real em transmissão ao vivo.
- Alterar o resultado validado das tasks anteriores.

## Critérios de aceite

- [x] `IChatResponseSender` existe no Application; Domain e Application não conhecem HTTP/SSN.
- [x] Captura e escrita usam clientes HTTP, options, DI e estado separados.
- [x] `ChatResponses:Enabled` e `AutoPlayInteractions` (Interactions e Narration) com default `false`.
- [x] Anti-loop: eco do texto escrito é marcado na captura e a decisão vira `Ignore/SelfMessage`.
- [x] Idempotência: mesma chave nunca é enviada duas vezes.
- [x] Isolamento de falha coberto por teste para cada modo de falha.
- [x] Nenhum banco, repositório, migration ou novo container.
- [x] `ChatResponses:Enabled` e `AutoPlayInteractions` restaurados para `false` ao final.
- [x] >= 40 testes novos; suíte completa verde.
- [x] Build Release com 0 erros e 0 warnings.
- [x] Secret scan sem achados.
- [x] Swagger válido contendo os novos endpoints.

## Testes/validações

- [x] `dotnet test ObsLiveBot.slnx -c Release`
- [x] `dotnet build ObsLiveBot.slnx -c Release`
- [x] `GET /swagger/v1/swagger.json` contém os endpoints de chat response
- [x] `GET /health` continua respondendo e expõe o novo subsistema
- [x] Secret scan do diff e do working tree

## Restrições

- Executar somente esta task.
- Preservar trabalho existente.
- Não avançar para a próxima task.

## Saída
TASK: OBS-LIVE-BOT-11 — Written Chat Responses (IChatResponseSender boundary)
RESULT: PASS
FILES CHANGED:
- `src/dotnet/ObsLiveBot.Domain/Chat/ChatResponseModels.cs`, `ChatProviderCapability.cs` (regras puras, sem HTTP/options/clock)
- `src/dotnet/ObsLiveBot.Contracts/Chat/ChatResponseContracts.cs`
- `src/dotnet/ObsLiveBot.Application/Abstractions/ChatResponseAbstractions.cs` (fronteira `IChatResponseSender`, ledger, registry de identidade, store de settings, writer)
- `src/dotnet/ObsLiveBot.Application/ChatResponses/` (gatekeeper, ledger, cooldowns, self-identity registry, settings store, writer/BackgroundService, handler MediatR, queries, notificações, capability queries)
- `src/dotnet/ObsLiveBot.Application/Features/ChatResponses/` (DevSend, Settings)
- `src/dotnet/ObsLiveBot.Application/LiveChat/LiveChatIngestionPipeline.cs` (marcação do eco no anti-loop)
- `src/dotnet/ObsLiveBot.Infrastructure/ChatResponseDependencyInjection.cs`, `ChatResponses/`, `Health/ChatResponseHealthCheck.cs`
- `src/dotnet/ObsLiveBot.Api/Features/ChatResponses/ChatResponseEndpoints.cs`, `Program.cs`, `appsettings.json`
- `tests/ObsLiveBot.UnitTests/ChatResponses/` (13 classes), `tests/ObsLiveBot.UnitTests/Api/ChatResponseEndpointTests.cs`, `tests/ObsLiveBot.UnitTests/Configuration/ShippedConfigurationTests.cs`
- `docs/CHAT-RESPONSES.md`, `docs/ARCHITECTURE.md`, `docs/LIVE-CHAT.md`, `docs/SOCIAL-STREAM-NINJA.md`, `docs/EXECUTION-PLAN.md`, `README.md`, `PROJECT-STATE.md`

TESTS: 712/712 PASS (298 novos contra o baseline 414 da Task10.2; 267 no subconjunto Task11). Build Release 0 erros / 0 avisos.

VALIDATION:
- `dotnet build ObsLiveBot.slnx -c Release`: 0 avisos, 0 erros
- `dotnet test ObsLiveBot.slnx -c Release`: 712 aprovados, 0 falhas
- `GET /swagger/v1/swagger.json`: válido, com os seis endpoints de chat response
- `GET /health`: expõe `chatResponses: healthy` (desligado é saudável); `obs`/`interactions` degradados apenas por isolamento intencional
- Anti-loop PASS: eco marcado com `interaction.generatedByStudioOS` → decisão `SelfMessage`; identidade aprendida no primeiro eco; gate recusa a própria conta do StudioOS
- Idempotência PASS: mesma chave nunca enviada duas vezes; `Forget` libera a chave após falha; `DUPLICATE_TEXT` confirmado em runtime, com escopo por canal
- Estado bounded PASS: anéis de idempotência, eco, cooldown, histórico e identidades todos limitados e com evict do mais antigo
- Isolamento de falha PASS: fila cheia, writer parado, sender ausente, exceção, timeout, cancelamento e notificação falhando viram resultado registrado; nada no caminho automático lança
- Capacidade de provider PASS: leitura e escrita reportadas separadamente, com código de erro real quando o transporte não responde
- `ChatResponses:Enabled=false` e `AutoPlayInteractions=false` (Interactions e Narration) confirmados ao final
- Ollama Docker autoritativo: container da API com `Interactions__Ollama__BaseUrl=http://ollama:11434`, verificado por inspeção somente leitura; Task11 não introduziu dependência do host nem tocou no Compose
- Sem banco, EF Core, repositório, migration ou container novo
- Secret scan: 0 achados nos arquivos novos e no diff; `git diff --check` limpo

PENDING: nenhum item de implementação. A escrita em live real não foi executada e permanece uma decisão do analista.

NOTES:
- O sender real do SSN 0.4.18 não tem comando de envio dedicado; a interação de página é a única rota suportada e é usada com `confirm: true`.
- Retentativa é deliberadamente fail-closed: só condições anteriores a qualquer digitação são repetidas. Depois do fill aceito a sequência nunca se repete, para que nenhuma mensagem pública seja publicada duas vezes.
- A identificação do composer exige evidência positiva no nome acessível e falha fechada; search, login e input de linha única sem nome nunca são digitados.
- O texto só é registrado como nosso depois de a plataforma aceitá-lo, então o eco de uma escrita que não aconteceu nunca é suprimido.
- Dois artefatos de rascunho da sessão anterior foram removidos antes do commit: `tests/ObsLiveBot.UnitTests/ChatResponses/ScratchDebugTests.cs` (vazio) e `data/task11-api.*.log`.
- Um literal de caractere de controle 0x1F embutido no código-fonte do ledger foi substituído pela constante `Separator = '\u001F'`, alinhada ao padrão já usado nos demais anéis.
- O container `obs-live-bot-api` em execução usa imagem anterior à Task11; reconstruir a imagem é ato de deployment e está fora do escopo desta task.
