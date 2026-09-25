# PROJECT — OBS Live Bot / Live Command Center

## Objetivo
Plataforma local Windows para OBS Studio que reúne automação de live, chat, TTS, IA opcional, criação de conteúdo, Command Center e backup/restore integral do ambiente OBS do usuário.

## Arquitetura oficial
O núcleo é C#/.NET com ASP.NET Core, Vertical Slice Architecture, CQRS, Mediator, Domain Model, Domain Events, EF Core/Migrations, validação e mapping. Python é reservado a IA/ML e processamento especializado de mídia. C++ é reservado a integração nativa e workloads de baixa latência/alto desempenho quando houver justificativa técnica. n8n é orquestrador, nunca fonte de verdade do domínio.

## Estado
Projeto em andamento. Não reinicializar, recriar a fundação ou reexecutar tasks concluídas sem solicitação explícita.

## Fonte de verdade
`PROJECT-STATE.md`, `AI-WORKFLOW.md`, `docs/` e a task solicitada.

## Compatibilidade multi-IA
- Codex: `AGENTS.md`
- Claude Code: `CLAUDE.md`
- GitHub Copilot: `.github/copilot-instructions.md`

## Restrições críticas
- Projeto independente do GFM TruckHub.
- Executar somente a task solicitada.
- OBS_CONFIG_ROOT permanece somente leitura em tasks normais.
- Escrita/restauração do OBS somente em task explícita de Backup/Restore/OBS Configuration, com OBS fechado, validação, backup de segurança e rollback.
- Segredos nunca são versionados no Git.
- Backups podem e devem preservar credenciais/segredos do ambiente OBS quando disponíveis, mas o pacote deve ser protegido/criptografado.
- IA permanece opcional e integrações substituíveis.
