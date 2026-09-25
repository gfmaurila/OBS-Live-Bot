# CLAUDE.md — OBS Live Bot / Live Command Center

Leia nesta ordem: `PROJECT.md`, `PROJECT-STATE.md`, `AI-WORKFLOW.md`, `docs/ARCHITECTURE.md`, `docs/EXECUTION-PLAN.md`, documentação relevante e task solicitada.

Não recrie scaffold nem reexecute tasks concluídas. Execute somente a task atual.

## Arquitetura obrigatória
C#/.NET é o núcleo: ASP.NET Core, Vertical Slice, CQRS, Mediator, Domain Model, Domain Events, EF Core/Migrations, validação, mapping, DI e structured logging. Python é serviço especializado de IA/mídia. C++ é camada nativa/performance somente com justificativa. n8n é orquestrador.

Endpoints finos -> mapping -> Command/Query -> Mediator/pipeline -> Handler -> Domain/Infrastructure -> mapping -> Response. Commands alteram estado; Queries não. Domain Events desacoplam efeitos de fatos relevantes.

`OBS_CONFIG_ROOT` é read-only por padrão. Somente tasks explícitas de Backup/Restore/OBS Configuration podem autorizar escrita. O módulo 06 fará backup integral do ambiente OBS do usuário, incluindo credenciais armazenadas quando portáveis, em pacote protegido. Ao encerrar OBS, um Job/Worker deve disparar o backup automático.

A task executável atual continua sendo `OBS-LIVE-BOT-02 — OBS WebSocket Connection`.
