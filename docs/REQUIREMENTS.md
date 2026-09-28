# Requisitos consolidados

## Produto
Plataforma local Windows para OBS com Live Engine, Content Engine, Command Center e Configuration/Backup/Restore.

## Arquitetura obrigatória C#
ASP.NET Core, Vertical Slice Architecture, CQRS, MediatR oficial, Domain Model, Domain Events, Validation, Mapping, DI e structured logging. EF Core fica disponível somente quando uma feature exigir persistência; nenhum provider de banco é obrigatório.

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
Operação local por padrão, custo de infraestrutura zero, componentes substituíveis, logs sem segredos, persistência somente quando necessária, validação, integridade, idempotência onde aplicável e nenhuma escrita acidental no OBS.

## Regra de execução atual
A presença destes requisitos não autoriza implementação antecipada. A próxima task é `OBS-LIVE-BOT-02 — OBS WebSocket Connection`.
