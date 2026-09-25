# GitHub Copilot — OBS Live Bot / Live Command Center

Leia `PROJECT.md`, `PROJECT-STATE.md`, `AI-WORKFLOW.md`, `docs/ARCHITECTURE.md` e `docs/EXECUTION-PLAN.md`. Não recrie a fundação e trabalhe somente na task solicitada.

C#/.NET é o núcleo e usa obrigatoriamente Vertical Slice + CQRS + Mediator + Domain Model + Domain Events + EF Core/Migrations + validation + mapping + DI + structured logging. Endpoints finos; Commands alteram estado; Queries somente leem; Handlers coordenam; regras ficam no Domain; contratos externos ficam em Infrastructure/Contracts; não exponha entidades diretamente.

Python: IA/ML e mídia. C++: nativo/performance somente com justificativa. n8n: orquestração, não domínio.

OBS_CONFIG_ROOT é read-only por padrão. Exceção somente em tasks explícitas de Backup/Restore/OBS Configuration. O módulo 06 deve suportar backup integral do OBS do usuário, inclusive credenciais armazenadas/portáveis, protegido/criptografado, e Job automático no encerramento do OBS.

Nunca hardcode ou versione segredos.
