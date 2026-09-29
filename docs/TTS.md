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

## Futuro, não implementado

- distribuição licenciada do runtime/voz;
- outras vozes e engine Windows alternativa;
- habilitação deliberada de autoplay de interações (desativado por padrão; configuração posterior);
- provider cloud opcional.
