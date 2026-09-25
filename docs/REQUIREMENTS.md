# Requisitos consolidados

## Produto
Plataforma local Windows para OBS com Live Engine, Content Engine, Command Center e Configuration/Backup/Restore.

## Arquitetura obrigatória C#
ASP.NET Core, Vertical Slice Architecture, CQRS, Mediator, Domain Model, Domain Events, EF Core/Migrations, Validation, Mapping, DI e structured logging.

## Requisitos funcionais
- OBS WebSocket e detecção de estado;
- chats YouTube/Twitch/Kick, welcome, fila, anti-spam, perguntas, timeout humano, respostas e TTS;
- IA opcional;
- Content Engine para transcrição, momentos, cortes e conteúdo YouTube/TikTok/Instagram;
- Command Center Windows;
- settings centralizados;
- backup/restore integral do ambiente OBS do usuário;
- Job automático de backup sempre que o OBS for fechado;
- preservar no snapshot todos os dados existentes no OBS_CONFIG_ROOT, inclusive credenciais armazenadas/portáveis;
- pacote com segredos protegido/criptografado, manifest e checksums;
- restore seguro com OBS fechado, backup prévio e rollback.

## Não funcionais
Operação local por padrão, componentes substituíveis, logs sem segredos, persistência local, validação, integridade, idempotência onde aplicável e nenhuma escrita acidental no OBS.

## Regra de execução atual
A presença destes requisitos não autoriza implementação antecipada. A próxima task é `OBS-LIVE-BOT-02 — OBS WebSocket Connection`.
