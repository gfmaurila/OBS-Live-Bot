# Plano de execução

Cada task deve ser executada e validada isoladamente. Uma task não autoriza antecipar funcionalidades das etapas seguintes.

| Task | Nome | Resultado esperado |
|---|---|---|
| OBS-LIVE-BOT-00 | Project Foundation | Estrutura, documentação, configuração-base e regras de segurança. |
| OBS-LIVE-BOT-01 | Docker + n8n local | Ambiente local do n8n em `127.0.0.1:5679`, com healthcheck e persistência validados. |
| OBS-LIVE-BOT-02 | OBS WebSocket Connection | Conexão autenticada e segura com OBS WebSocket. |
| OBS-LIVE-BOT-03 | Live State Detection | Detecção confiável do estado da transmissão. |
| OBS-LIVE-BOT-04 | Primeiro Chat Connector | Primeiro conector de chat atrás de contrato comum. |
| OBS-LIVE-BOT-05 | Welcome Engine | Identificação de primeira interação e boas-vindas. |
| OBS-LIVE-BOT-06 | TTS Engine | Provedor de TTS desacoplado e configurável. |
| OBS-LIVE-BOT-07 | Message Queue + Anti-Spam | Fila, cooldown, limites e proteção contra abuso. |
| OBS-LIVE-BOT-08 | Question Detection | Detecção de mensagens direcionadas ao streamer. |
| OBS-LIVE-BOT-09 | Human Response Timeout | Janela configurável para resposta humana. |
| OBS-LIVE-BOT-10 | Automatic Response Engine | Respostas automáticas baseadas primeiro em regras locais. |
| OBS-LIVE-BOT-11 | Optional AI Provider | Porta e provedor de IA opcionais. |
| OBS-LIVE-BOT-12 | YouTube + Twitch + Kick | Conectores das três plataformas sob o contrato comum. |
| OBS-LIVE-BOT-13 | OBS Audio Integration | Encaminhamento seguro do áudio gerado para o OBS. |
| OBS-LIVE-BOT-14 | End-to-End Live Tests | Testes integrados em cenário controlado de live. |

## Critérios de `OBS-LIVE-BOT-00`

- Estrutura inicial criada.
- Responsabilidades documentadas.
- Arquitetura desacoplada definida.
- Compose preparado, mas não executado.
- `.env.example` sem segredos.
- Diretório de configuração do OBS preservado.

## Próxima etapa

`OBS-LIVE-BOT-01 — Docker + n8n local`

Essa etapa deverá avaliar a versão da imagem, criar o `.env` local, iniciar o serviço, validar persistência e acesso local e documentar a operação. Ela não faz parte da fundação atual.
