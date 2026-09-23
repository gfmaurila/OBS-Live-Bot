# Requisitos

## Objetivo do produto

O OBS Live Bot será uma automação local para auxiliar transmissões no OBS Studio sem depender obrigatoriamente de serviços cloud.

## Requisitos funcionais futuros

As funcionalidades abaixo orientam a arquitetura, mas não fazem parte de `OBS-LIVE-BOT-00`:

- detectar quando uma live estiver ativa;
- integrar com chats de YouTube, Twitch e Kick;
- identificar a primeira interação de cada usuário;
- enviar boas-vindas automaticamente;
- detectar mensagens direcionadas ao streamer;
- reproduzir mensagens por TTS;
- aguardar um tempo configurável para resposta humana;
- responder automaticamente quando o streamer não responder;
- reproduzir respostas automáticas por áudio;
- manter fila de mensagens;
- aplicar cooldown e proteção contra spam;
- utilizar regras locais sempre que possível;
- permitir IA como recurso opcional;
- executar n8n localmente via Docker.

## Requisitos não funcionais

- Operação local por padrão.
- Componentes desacoplados e substituíveis.
- Segredos somente em configuração local não versionada.
- Persistência local para n8n e dados operacionais.
- Logs sem credenciais, tokens ou conteúdo sensível desnecessário.
- Falhas de serviços opcionais não devem impedir regras locais essenciais.
- Configurações do OBS protegidas contra escrita acidental.

## Restrições da fundação

- Não alterar o diretório de configuração do OBS.
- Não implementar conectores de chat.
- Não implementar TTS.
- Não implementar IA.
- Não criar workflows funcionais do n8n.
- Não iniciar serviços nem instalar software no Windows.

