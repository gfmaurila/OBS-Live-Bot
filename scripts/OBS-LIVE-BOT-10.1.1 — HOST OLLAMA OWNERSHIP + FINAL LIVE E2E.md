TASK:
OBS-LIVE-BOT-10.1.1 — HOST OLLAMA OWNERSHIP + FINAL LIVE E2E

PRODUCT:
GFM StudioOS

==================================================
1. CONTEXT
==================================================

Esta task é CONTINUAÇÃO da:

OBS-LIVE-BOT-10.1 — CONTAINERIZED LOCAL AI RUNTIME

NÃO refazer Task10.1.
NÃO refatorar o que já está funcionando.
NÃO iniciar Task11.

Último estado validado da Task10.1:

TECHNICAL RESULT:
PASS

OLLAMA DOCKER:
PASS

CONTAINER:
gfm-studioos-ollama

MODEL:
qwen3:4b-instruct-2507-q4_K_M

MODEL PERSISTENCE:
PASS

MODEL REDOWNLOAD AFTER RECREATE:
NO

API → OLLAMA DOCKER:
PASS — http://ollama:11434

REAL INFERENCE:
PASS

HOST OLLAMA DEPENDENCY:
reportado anteriormente como NONE

GPU:
NVIDIA GeForce RTX 3060 12 GB

GPU AVAILABLE TO OLLAMA:
YES

GPU USED BY MODEL:
YES — validação anterior mostrou 100% GPU

CPU FALLBACK:
PASS

FAILURE ISOLATION:
PASS

RECOVERY:
PASS

AUTOPLAY:
FALSE

SSN:
Healthy

PIPER:
Ready

N8N:
Healthy

TESTS:
292/292 PASS

BUILD:
0 errors / 0 warnings

SECRET SCAN:
PASS

LAST COMMIT:
cd5028cbf522a144693a46482a2cf56be20548c3

PUSH:
PASS

REMOTE == LOCAL:
YES

GIT:
CLEAN

WINDOWS OLLAMA UNINSTALLED:
NO

==================================================
2. IMPORTANT NEW FINDING
==================================================

Durante a preparação do FINAL LIVE E2E foi encontrada
uma inconsistência que precisa ser resolvida ANTES de
qualquer interação automática.

Foi observado no Windows:

host port 11434:
LISTENING

bindings observados:
0.0.0.0 / ::

PID observado naquele momento:
30308

Também foi observado:

http://127.0.0.1:11434/api/tags

respondendo e mostrando o modelo Qwen.

IMPORTANTE:

NÃO assumir que PID 30308 ainda é o PID atual.

Identificar novamente o listener atual.

O Codex atingiu o limite de uso justamente durante
a identificação do proprietário do PID.

Nenhuma correção foi aplicada.

AutoPlay NÃO foi habilitado.

==================================================
3. DOCKER EVIDENCE
==================================================

O Docker Desktop foi inspecionado visualmente.

Container:

gfm-studioos-ollama

estava RUNNING.

Na coluna Port(s), o container Ollama NÃO apresentava
mapeamento de porta para o host.

Outros containers mostravam normalmente seus mappings,
por exemplo:

API:
5080:8080

Social Stream Ninja:
8080:18080

n8n:
5679:5678

Mas:

gfm-studioos-ollama:
nenhum Port(s) exibido

Portanto NÃO assumir que o listener Windows :11434
é publicação do container.

Confirmar tecnicamente.

Também havia:

gfm-studioos-ollama-model

parado após provisionamento, comportamento esperado para
o provisionador one-shot.

==================================================
4. PRIMARY OBJECTIVE
==================================================

Primeiro determinar EXATAMENTE quem está ouvindo a
porta 11434 do Windows.

Depois comprovar novamente que:

StudioOS API
    ↓
Docker network
    ↓
http://ollama:11434
    ↓
gfm-studioos-ollama
    ↓
Qwen

não depende do Ollama instalado no Windows.

Somente depois dessa prova:

realizar o FINAL REAL LIVE E2E da Task10.1.

==================================================
5. SAFETY FIRST
==================================================

ANTES de qualquer coisa:

AutoPlayInteractions MUST remain FALSE.

NÃO:

- iniciar interação automática;
- enviar mensagem sintética;
- iniciar/parar stream;
- iniciar/parar gravação;
- alterar OBS;
- alterar OAuth;
- alterar SSN sem necessidade;
- alterar Piper;
- alterar n8n;
- alterar gfm-truckhub-n8n;
- desinstalar Ollama Windows;
- matar PID desconhecido;
- iniciar Task11.

==================================================
6. PHASE A — PORT 11434 OWNERSHIP
==================================================

Identificar novamente o estado ATUAL.

Não reutilizar cegamente PID 30308.

Determinar:

1. Host port 11434 está ouvindo?

2. Em quais interfaces?

3. Qual PID atual possui o listener?

4. Qual processo?

5. Qual executável?

6. Qual serviço, se aplicável?

7. Qual command line, se necessário e seguro?

Classificar proprietário:

A.
WINDOWS OLLAMA

B.
DOCKER PORT FORWARD / DOCKER DESKTOP

C.
OUTRO PROCESSO

D.
SEM LISTENER

Não modificar nada antes da classificação.

==================================================
7. PHASE B — DOCKER PORT VERIFICATION
==================================================

Inspecionar tecnicamente:

gfm-studioos-ollama

Confirmar:

- exposed ports;
- published ports;
- effective Docker port bindings;
- Compose configuration;
- Docker network;
- aliases/service DNS.

Determinar explicitamente:

OLLAMA CONTAINER PORT PUBLISHED TO WINDOWS HOST:
YES / NO

Se YES:

informar mapping exato.

Se NO:

não modificar simplesmente por preferência;
usar a informação para identificar o listener real.

==================================================
8. PHASE C — API EFFECTIVE ENDPOINT
==================================================

Confirmar configuração EFETIVA da API em execução.

Esperado:

http://ollama:11434

Não basta apenas encontrar esse valor em arquivo.

Quando possível, comprovar que a API/container resolve
e comunica com:

ollama:11434

pela Docker network.

Reportar:

API EFFECTIVE OLLAMA ENDPOINT:
<value>

API USING HOST.DOCKER.INTERNAL FOR OLLAMA:
YES / NO

API USING LOCALHOST FOR OLLAMA:
YES / NO

API USING DOCKER SERVICE DNS:
YES / NO

==================================================
9. PHASE D — IF WINDOWS OLLAMA IS RUNNING
==================================================

Somente se a Phase A comprovar que o listener pertence
ao Ollama nativo do Windows:

1. identificar como ele foi iniciado;
2. parar o Ollama Windows de forma segura;
3. NÃO desinstalar;
4. confirmar que processo nativo desapareceu;
5. confirmar que listener nativo desapareceu.

IMPORTANTE:

Se a porta 11434 continuar ouvindo após parar o Ollama
Windows, investigar novamente.

Não matar processos Docker.

Não assumir sucesso baseado apenas em nome de processo.

==================================================
10. PHASE E — HOST INDEPENDENCE REVALIDATION
==================================================

Com Ollama Windows comprovadamente parado:

validar diretamente dentro do ambiente Docker:

StudioOS API
    ↓
ollama:11434
    ↓
gfm-studioos-ollama

Executar uma inferência REAL.

Modelo:

qwen3:4b-instruct-2507-q4_K_M

Não usar fallback como substituto.

Confirmar:

WINDOWS OLLAMA RUNNING:
NO

DOCKER OLLAMA:
RUNNING

API ENDPOINT:
http://ollama:11434

REAL INFERENCE:
PASS

HOST OLLAMA DEPENDENCY:
NONE

Se possível, validar novamente:

ollama ps

e determinar processor:

GPU / CPU

Não exigir modelo carregado permanentemente apenas para
considerar o serviço saudável; diferenciar container
healthy de modelo atualmente carregado.

==================================================
11. PHASE F — PRE-LIVE REGRESSION CHECK
==================================================

Confirmar:

- API healthy;
- Ollama Docker Ready;
- required Qwen model available;
- Piper Ready;
- SSN Healthy/Connected;
- OBS WebSocket Connected/Synchronized quando OBS estiver aberto;
- Narration Ready;
- AutoPlay FALSE;
- n8n healthy;
- Twitch/Kick configuration preserved.

Do not modify:

gfm-truckhub-n8n

==================================================
12. YOUTUBE CURRENT LIVE
==================================================

Somente quando o analista informar que existe uma nova
live de teste disponível.

A Task10 já comprovou:

SSN /live_chat

como rota funcional neste ambiente.

A descoberta automática da live atual NÃO foi implementada.

Portanto:

NÃO assumir que a source antiga aponta para a nova live.

Source anteriormente canônica:

youtube-url-1t69en

era associada à transmissão anterior.

Para uma nova transmissão:

- identificar URL/ID atual pelos mecanismos já permitidos;
- configurar/ensure SSN /live_chat;
- usar implementação existente;
- idempotência por vídeo;
- evitar duplicata;
- não criar scraping customizado;
- não implementar descoberta automática;
- preservar OAuth/session;
- preservar Twitch/Kick.

==================================================
13. INPUT-ONLY VALIDATION
==================================================

ANTES de AutoPlay:

validar uma mensagem real chegando por:

YouTube
   ↓
SSN /live_chat
   ↓
SSE
   ↓
Normalization
   ↓
LiveChatBuffer

Não aceitar mensagem sintética como prova.

Se input real NÃO funcionar:

STOP.

AutoPlay deve permanecer FALSE.

==================================================
14. REAL INTERACTION CHECKPOINT
==================================================

Somente após todas as fases anteriores PASS:

habilitar temporariamente:

AutoPlayInteractions = TRUE

Então imprimir:

READY FOR REAL MESSAGE

E STOP.

Aguardar o analista.

Não inventar mensagem.
Não injetar mensagem.
Não continuar automaticamente.

==================================================
15. MESSAGE TO BE SENT BY ANALYST
==================================================

Após READY FOR REAL MESSAGE, o analista enviará UMA
mensagem real no chat da live.

Sugestão:

!studio me dá um alô para testar o áudio

O Codex NÃO deve enviar essa mensagem.

==================================================
16. FINAL E2E
==================================================

Validar exatamente uma cadeia:

YouTube Live real
    ↓
SSN /live_chat
    ↓
SSE
    ↓
LiveChatGateway
    ↓
Normalization
    ↓
InteractionDecisionPolicy
    ↓
Ollama DOCKER
    ↓
qwen3:4b-instruct-2507-q4_K_M
    ↓
Sanitizer
    ↓
Piper
    ↓
WAV
    ↓
Narration Queue
    ↓
OBS
    ↓
YouTube Live

WINDOWS OLLAMA MUST remain STOPPED.

==================================================
17. EVIDENCE
==================================================

Registrar:

- real provider message ID;
- interaction ID;
- narration ID;
- correlation ID;
- accepted trigger;
- Ollama request count;
- effective Ollama endpoint;
- Docker Ollama confirmation;
- Windows Ollama state;
- fallback YES/NO;
- GPU/CPU if observable;
- Piper request count;
- valid WAV;
- WAV format;
- narration queued;
- narration started;
- narration completed;
- OBS playback started;
- OBS playback completed;
- failures;
- overlap;
- synthetic event YES/NO.

==================================================
18. USER AUDIBILITY
==================================================

OBS playback technical success is NOT sufficient.

After playback:

STOP.

Ask the analyst:

"Você ouviu o áudio na live?"

Do not mark complete functional E2E until explicit answer.

Only after explicit analyst confirmation:

USER HEARD AUDIO:
YES

==================================================
19. CLEANUP
==================================================

Immediately after the interaction attempt:

AutoPlayInteractions = FALSE

Confirm effective value.

Windows Ollama must remain stopped.

Do not uninstall Windows Ollama.

Do not leave test automation armed.

==================================================
20. HOST OLLAMA UNINSTALL DECISION
==================================================

Only after:

- ownership investigation;
- Windows Ollama stopped;
- API uses Docker DNS;
- real Docker inference;
- final real YouTube E2E;
- analyst hears audio;

may the report state:

SAFE TO UNINSTALL HOST OLLAMA:
YES

DO NOT actually uninstall it.

Uninstallation requires analyst authorization after review.

==================================================
21. TEST / BUILD POLICY
==================================================

This continuation should not modify application code unless
an actual defect requiring correction is discovered.

If NO code/config changes are needed:

do not create a meaningless commit.

Use existing:

cd5028cbf522a144693a46482a2cf56be20548c3

as Task10.1 implementation commit.

If a legitimate correction is required:

- make minimum change;
- run relevant tests;
- run full test suite;
- build;
- secret scan;
- git diff --check;
- review diff/status;
- update docs if behavior changed;
- commit;
- push;
- verify remote == local;
- clean tree.

Baseline:

292/292 tests PASS
0 errors
0 warnings

Do not regress.

==================================================
22. SECURITY
==================================================

Do not print:

- OBS WebSocket password;
- OAuth tokens;
- Google secrets;
- session secrets;
- DPAPI contents;
- n8n encryption secrets.

Run secret scan if repository changes.

OBS WebSocket credential rotation remains a separate
mandatory follow-up.

DO NOT rotate it during this task.

==================================================
23. INTERMEDIATE REPORT
==================================================

After host ownership + independence validation, but BEFORE
the live interaction, report:

TASK:
OBS-LIVE-BOT-10.1.1 — HOST OLLAMA OWNERSHIP + FINAL LIVE E2E

PORT 11434:
LISTENING / NOT LISTENING

OWNER PID:
<value>

OWNER PROCESS:
<value>

OWNER EXECUTABLE:
<value>

OWNER TYPE:
WINDOWS OLLAMA / DOCKER PORT FORWARD / OTHER / NONE

OLLAMA CONTAINER HOST PORT:
PUBLISHED / NOT PUBLISHED

MAPPING:
<value>

WINDOWS OLLAMA:
RUNNING / STOPPED / NOT INSTALLED

DOCKER OLLAMA:
<status>

API EFFECTIVE ENDPOINT:
<value>

API USING DOCKER DNS:
YES / NO

REAL DOCKER INFERENCE:
PASS / FAIL

HOST OLLAMA DEPENDENCY:
NONE / PRESENT / UNKNOWN

GPU:
<status>

AUTOPLAY:
FALSE

OBS:
<status>

SSN:
<status>

PIPER:
<status>

N8N:
<status>

READY FOR LIVE INPUT CHECK:
YES / NO

Then continue only if a live is actually available.

==================================================
24. FINAL REPORT
==================================================

TASK:
OBS-LIVE-BOT-10.1.1 — HOST OLLAMA OWNERSHIP + FINAL LIVE E2E

RESULT:
PASS / PARTIAL / FAIL

HOST PORT 11434:
<status>

PORT OWNER:
<details>

OLLAMA CONTAINER PORT PUBLISHED:
YES / NO

WINDOWS OLLAMA RUNNING DURING E2E:
YES / NO

DOCKER OLLAMA:
PASS / FAIL

MODEL:
qwen3:4b-instruct-2507-q4_K_M

API EFFECTIVE OLLAMA ENDPOINT:
<value>

HOST OLLAMA DEPENDENCY:
NONE / PRESENT

GPU USED:
YES / NO / UNKNOWN

REAL YOUTUBE INPUT:
PASS / FAIL

SSN LIVE_CHAT:
PASS / FAIL

REAL AI:
PASS / FAIL

REAL TTS:
PASS / FAIL

REAL WAV:
PASS / FAIL

REAL OBS PLAYBACK:
PASS / FAIL

USER HEARD AUDIO:
YES / NO / NOT TESTED

INTERACTION ID:
<id>

NARRATION ID:
<id>

CORRELATION ID:
<id>

SYNTHETIC EVENT:
YES / NO

AUTOPLAY FINAL:
FALSE / OTHER

OBS:
<status>

SSN:
<status>

OLLAMA:
<status>

PIPER:
<status>

N8N:
<status>

TESTS:
<passed>/<total or NOT RERUN with justification>

BUILD:
<result>

COMMIT:
<hash / NO NEW COMMIT REQUIRED>

PUSH:
PASS / NOT REQUIRED / FAIL

REMOTE == LOCAL:
YES / NO

GIT STATUS:
CLEAN / DIRTY

HOST OLLAMA UNINSTALLED:
NO

SAFE TO UNINSTALL HOST OLLAMA:
YES / NO

KNOWN LIMITATIONS:
- automatic YouTube current-live discovery remains not implemented
- <others actually observed>

SECURITY FOLLOW-UP:
OBS WebSocket credential rotation remains pending.

==================================================
25. PASS CRITERIA
==================================================

PASS requires:

- owner of Windows :11434 conclusively identified;
- no accidental host Ollama dependency;
- Windows Ollama stopped during final validation;
- API confirmed using Docker Ollama;
- real Docker Qwen inference PASS;
- current YouTube live input PASS;
- real AI PASS;
- real Piper PASS;
- real OBS playback PASS;
- analyst explicitly confirms hearing audio;
- AutoPlay returned FALSE;
- no synthetic substitute;
- Git remains clean;
- no regression introduced.

==================================================
26. STOP CONDITION
==================================================

DO NOT START TASK11.

DO NOT IMPLEMENT:

- written chat responses;
- official YouTube API;
- automatic YouTube live discovery;
- full installer;
- cloud AI providers.

DO NOT UNINSTALL WINDOWS OLLAMA.

After final report:

STOP.