# AGENTS.md — Codex

## Leitura obrigatória
`PROJECT.md` -> `PROJECT-STATE.md` -> `AI-WORKFLOW.md` -> `docs/ARCHITECTURE.md` -> `docs/EXECUTION-PLAN.md` -> documentação da task.

## Regra principal
EXECUTAR SOMENTE A TASK SOLICITADA. Não recriar a fundação e não antecipar módulos futuros.

## Ownership tecnológico
- **C#/.NET:** núcleo oficial, domínio, Vertical Slices, CQRS, Mediator, Domain Events, API, Command Center, EF Core, configurações, OBS Gateway, backup/restore e coordenação.
- **Python:** IA/ML, transcrição, análise/processamento de vídeo e áudio, detecção/classificação de momentos e metadata especializada.
- **C++:** somente quando tecnicamente justificado para integração nativa, plugin/extensão OBS, áudio de baixa latência ou processamento de alto desempenho. Não colocar regra de negócio em C++.
- **n8n:** orquestração, webhooks, agendas e integrações. Não é núcleo do domínio nem fonte de verdade.

## Padrão C# obrigatório
ASP.NET Core + Vertical Slice Architecture + CQRS + Mediator + Domain Model + Domain Events + EF Core/Migrations + FluentValidation (ou abstração equivalente aprovada) + Mapping Request/Command/Domain/Response + DI + structured logging.

Commands alteram estado; Queries somente leem. Endpoints são finos. Handlers coordenam casos de uso. Regras/invariantes ficam no Domain. Entidades de domínio não são expostas diretamente. Validação deve preferir pipeline do Mediator. Domain Events representam fatos relevantes já ocorridos.

## OBS e backup
`OBS_CONFIG_ROOT` é somente leitura por padrão. Exceção somente para tasks explícitas de Backup/Restore/OBS Configuration. O módulo 06 deve realizar backup integral do diretório OBS do usuário e pode preservar credenciais armazenadas pelo OBS. Backup com segredos deve ser protegido/criptografado e nunca versionado.

Ao fechar `obs64.exe`, o módulo 06 deverá possuir Job/Worker que dispara backup automático integral do OBS, com manifest/checksum, retenção configurável, prevenção de concorrência e preservação do último backup válido.

## Segurança
Nunca hardcode/versione senhas, tokens ou chaves. Logs não devem expor segredos. Não instalar software, iniciar/encerrar/reconfigurar OBS ou escrever no OBS sem autorização da task.
