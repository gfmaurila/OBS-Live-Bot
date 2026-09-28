# TTS Foundation

## Implementado

`ITextToSpeechProvider` recebe `TextToSpeechRequest` com interaction ID, texto já sanitizado, voz opcional, idioma e correlation ID. O retorno `TextToSpeechResult` contém status, provider, formato, caminho opcional, duração, erro e indicador de simulação.

`DevelopmentTextToSpeechProvider` é determinístico e local. Ele retorna sucesso com formato `development/simulated`, sem blob e sem arquivo. Nenhum áudio real é produzido ou reproduzido.

O TTS só é chamado para `Voice` e `TextAndVoice`. `Text` não chama TTS; `Ignore` não chama AI nem TTS.

## Segurança e memória

- O pipeline armazena somente metadados e caminho opcional, não blobs de áudio indefinidos no buffer.
- Texto passa pelo sanitizer antes do TTS.
- Não há reprodução automática no OBS.
- Não há engine, modelo, download ou serviço externo obrigatório.
- Falhas de TTS são isoladas e registradas como resultado explícito sem derrubar os demais subsistemas.

## Futuro, não implementado

- Windows/local TTS;
- Piper ou engine local equivalente;
- seleção real de vozes;
- cache/retention de arquivos de áudio;
- roteamento ou playback no OBS;
- provider cloud opcional.
