# AI WORKFLOW

Fluxo compartilhado por Codex, Claude Code e GitHub Copilot.

## Antes de alterar
1. Ler `PROJECT.md`.
2. Ler `PROJECT-STATE.md`.
3. Ler `docs/EXECUTION-PLAN.md`.
4. Ler documentação relacionada.
5. Ler a task solicitada.
6. Inspecionar o estado atual do repositório.

## Regra principal
EXECUTAR SOMENTE A TASK SOLICITADA.

Não reiniciar o projeto, reconstruir fundações concluídas ou antecipar etapas.

## Fluxo
RESEARCH -> REQUIREMENTS -> ARCHITECTURE -> PLAN -> IMPLEMENT -> TEST -> REVIEW -> DOCUMENT

Use somente as fases necessárias à task atual.

## Regras
- preservar comportamento fora do escopo;
- preservar alterações locais;
- não inventar requisitos;
- manter integrações desacopladas;
- manter segredos fora do Git;
- não contornar autenticação/configuração oficial;
- não alterar OBS/configuração sem autorização explícita.

## Saída obrigatória
TASK:
RESULT: PASS | PARTIAL | FAIL
FILES CHANGED:
TESTS:
VALIDATION:
PENDING:
NOTES:

Só atualizar estado como concluído após validação real.
