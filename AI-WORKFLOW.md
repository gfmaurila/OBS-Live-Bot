# AI WORKFLOW

Fluxo compartilhado por Codex, Claude Code e GitHub Copilot.

## Antes de alterar
1. Ler `PROJECT.md`.
2. Ler `PROJECT-STATE.md`.
3. Ler `docs/ARCHITECTURE.md` e `docs/EXECUTION-PLAN.md`.
4. Ler documentação relacionada.
5. Ler a task solicitada.
6. Inspecionar o estado real do repositório.

## Regra principal
EXECUTAR SOMENTE A TASK SOLICITADA. Documentação de módulos futuros não autoriza sua implementação.

## Fluxo
RESEARCH -> REQUIREMENTS -> ARCHITECTURE -> PLAN -> IMPLEMENT -> TEST -> REVIEW -> DOCUMENT

## Regras de arquitetura
- C# é default e proprietário do domínio.
- Vertical Slice + CQRS + Mediator + Domain Events + Validation + Mapping são padrões oficiais.
- Python somente para IA/ML/mídia especializada.
- C++ somente para nativo/performance justificada.
- n8n somente para orquestração.
- preservar comportamento fora do escopo e alterações locais.
- manter integrações desacopladas.
- segredos fora do Git; backups com segredos devem ser protegidos.
- não escrever no OBS sem autorização explícita da task.

## Saída obrigatória
TASK:
RESULT: PASS | PARTIAL | FAIL
FILES CHANGED:
TESTS:
VALIDATION:
PENDING:
NOTES:

Só atualizar estado como concluído após validação real.
