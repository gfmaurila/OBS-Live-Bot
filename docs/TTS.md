# Local TTS — OBS-LIVE-BOT-07

## Implementado

`ITextToSpeechProvider` recebe `TextToSpeechRequest` com interaction ID, texto já sanitizado, voz opcional, idioma e correlation ID. O retorno `TextToSpeechResult` contém status, provider, formato, caminho opcional, duração, erro e indicador de simulação.

## Implementado: Piper local real

`PiperTextToSpeechProvider` implementa `ITextToSpeechProvider` em Infrastructure. A API em Docker Compose executa o runtime local Linux x86_64 (Piper 1.2.0, release `2023.11.14-2`); os arquivos ficam no host em `data/runtime/tts-engine/`, montados no container em `/opt/tts-engine` como somente leitura. Não há cloud, custo por chamada nem download automático.

- Voz selecionada: `pt_BR-faber-medium`, português brasileiro, qualidade medium, uma voz, 22.050 Hz.
- Modelo local: 63.201.294 bytes (~60,3 MiB; diretório do modelo indica cerca de 63,3 MB).
- Saída verificada: WAV RIFF/WAVE, PCM little-endian, 22.050 Hz, mono, 16-bit.
- Artefato temporário: `data/runtime/tts/` no host (`/app/data/runtime/tts` no container). Nome usa o GUID da interação, nunca texto/display name.
- Cleanup bounded executa antes das sínteses: máximo de 100 arquivos e idade máxima de 60 minutos; limita escopo ao diretório configurado. A saída não mantém blobs no buffer de interações.
- Timeout configurado: 20 segundos; concorrência: 1; espera bounded: fila de 2 solicitações e no máximo 2 segundos. Sobrecarga e timeout retornam erro explícito.
- `CancellationToken` é propagado; processo é encerrado em cancelamento/timeout. Texto recebido pelo chat é enviado via stdin e argumentos de processo são adicionados individualmente (`ArgumentList`), sem shell.
- `AllowDevelopmentFallback` é explícito. O provider/result identifica quando houve fallback; falha de voz preserva a resposta de texto da IA.

Verificação do runtime no serviço: `docker exec obs-live-bot-api /opt/tts-engine/piper/piper --version`. Para smoke/integration real, no ambiente Development use `POST /api/interactions/dev/test` com `responseMode: TextAndVoice`; examine o resultado em `GET /api/interactions/recent`. O smoke desta Task gerou WAV de 170.772 bytes e duração de 3,871 s, validado estruturalmente.

## Licenças e distribuição

- **Engine Piper:** código-fonte sob MIT; release/runtime também inclui dependências, incluindo eSpeak NG sob GPL-3.0-or-later e ONNX Runtime sob MIT. Distribuição futura deve preservar notices e cumprir obrigações das dependências; revisão de compliance é necessária antes de empacotar o runtime.
- **Modelo de voz Faber:** origem do arquivo é `rhasspy/piper-voices`, diretório `pt/pt_BR/faber/medium`; o model card identifica a voz/dataset como CC0 e registra fine-tuning a partir da voz Lessac English. Isso não resolve com segurança todos os direitos sobre o modelo derivado. **Licença de redistribuição: UNKNOWN.** Uso local de desenvolvimento não equivale a autorização para redistribuir; não incluir o modelo em instaladores/pacotes até uma revisão independente confirmar os direitos.
- O tamanho/modelo não é incluído no Git. O runtime e a voz devem ser provisionados separadamente.

Fontes: [Piper license](https://github.com/rhasspy/piper/blob/master/LICENSE.md?plain=1), [Piper release](https://github.com/rhasspy/piper/releases), [Faber voice model](https://huggingface.co/rhasspy/piper-voices/tree/main/pt/pt_BR/faber/medium), [Faber model card](https://huggingface.co/rhasspy/piper-voices/blob/main/pt/pt_BR/faber/medium/MODEL_CARD), [eSpeak NG](https://github.com/espeak-ng/espeak-ng), [upstream discussion on voice licensing](https://github.com/rhasspy/piper/discussions/271).

`DevelopmentTextToSpeechProvider` continua registrado para testes e fallback. Ele retorna formato `development/simulated`, sem áudio real.

O TTS só é chamado para `Voice` e `TextAndVoice`. `Text` não chama TTS; `Ignore` não chama AI nem TTS.

## Segurança e memória

- O pipeline armazena somente metadados e caminho opcional, não blobs de áudio indefinidos no buffer.
- Texto passa pelo sanitizer antes do TTS.
- Não há reprodução automática no OBS.
- Nenhum engine/modelo é baixado automaticamente pela aplicação.
- Falhas de TTS são isoladas e registradas como resultado explícito sem derrubar os demais subsistemas.
- Ao concluir a Task 07, **REAL LOCAL TTS: YES; OBS AUDIO PLAYBACK: NOT YET**: aquela task não reproduziu nem roteou áudio. A Task 08 acrescentou a source de narração dedicada e confirmou playback real no OBS, descrito em [`NARRATION.md`](NARRATION.md); isso não habilita autoplay de chat nem monitoramento local.

## Duas vozes — OBS-LIVE-BOT-10.2

Uma interação agora pode produzir dois clipes, e cada um recebe seu próprio identificador de artefato e seu próprio arquivo WAV. O papel é semântico e nunca é inferido pelo nome do arquivo do modelo:

| Papel | Texto | Voz | Ordem |
|---|---|---|---|
| `Chat` | mensagem determinística do chat, sem o trigger | `pt_BR-jeff-medium` | 1 |
| `Assistant` | resposta gerada pelo modelo local | `pt_BR-faber-medium` | 2 |

Os dois clipes de uma mesma interação recebem IDs de artefato distintos por construção: o papel é combinado nos próprios bits do GUID, e não anexado fora dele. Sem essa separação os dois papéis apontariam para o mesmo WAV e um sobrescreveria o outro.

`PiperVoiceCatalog` resolve a voz solicitada por papel dentro de `Interactions:Tts:VoicesDirectory`, exige que o modelo e sua configuração existam e rejeita identificadores com separadores de caminho, extensões ou outros caracteres inseguros. O Piper deriva a configuração da voz como o caminho do modelo mais `.json`, portanto o arquivo real de configuração é `<voz>.onnx.json`; o provider usa esse padrão adjacente em vez de passar `--config`, o que preserva a compatibilidade de argumentos já validada.

As requisições concorrentes de TTS continuam limitadas pela configuração existente (concorrência 1, fila bounded de 2, espera máxima de 2 s, timeout de 20 s). A voz do `Chat` é resolvida uma vez por configuração e pode receber volume lógico próprio, aplicado imediatamente antes da reprodução daquele papel.

O texto recebido do chat nunca é concatenado em argumentos de processo: ele continua sendo enviado exclusivamente por stdin.

### Modelo de voz Chat

`pt_BR-jeff-medium`, português brasileiro, qualidade medium, 22.050 Hz, 62.950.044 bytes. SHA-256 do modelo: `3a6f4c46355813c2b7bbc4d16b6d13d60ed72074b952a393baace82a7d0c94b5`. Origem `rhasspy/piper-voices`, diretório `pt/pt_BR/jeff/medium`; o model card identifica o dataset como **CC0** e o repositório como **MIT**. A redistribuição do arquivo de modelo permanece **UNKNOWN** pelo mesmo motivo de Faber: a licença do dataset CC0 não resolve, sozinha, todos os direitos sobre o modelo derivado. O modelo não é versionado no Git e deve ser provisionado separadamente.

### Estado de validação

O suporte às duas vozes está implementado e coberto por testes automatizados (414 testes unitários; build Release com 0 erros e 0 avisos). As duas vozes foram confirmadas como aceitas pelo Piper 1.2.0 em execução local sem emitir áudio para a transmissão, e os WAVs de sondagem foram removidos. **A reprodução audível das duas vozes no OBS NÃO foi realizada**: `Narration:AutoPlayInteractions` foi mantido em `false` nesta fase.

## Futuro, não implementado

- distribuição licenciada do runtime/voz;
- outras vozes e engine Windows alternativa;
- reprodução audível das duas vozes, que depende de autorização explícita do analista e de `AutoPlayInteractions=true`;
- habilitação deliberada de autoplay de interações (desativado por padrão; configuração posterior);
- provider cloud opcional.
