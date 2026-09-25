# Language Ownership — regra para todas as IAs

## C#/.NET — default
Use para domínio, CQRS, Mediator, Domain Events, API, Command Center, EF Core, settings, OBS WebSocket, backup/restore, coordenação e contratos.

## Python — especializado
Use para IA/ML, transcrição e análise/processamento de mídia quando o ecossistema Python trouxer vantagem concreta. Não mover domínio, banco principal ou configuração global para Python.

## C++ — excepcional
Use somente com justificativa técnica para nativo/Windows, plugin OBS, áudio de baixa latência ou performance pesada. Não implementar regra de negócio.

## n8n — orquestração
Use para workflows, webhooks, schedules e integrações. Não usar como fonte de verdade ou substituto do domínio C#.

## Regra de decisão
Se a task não especificar linguagem: C# primeiro; Python somente IA/mídia; C++ somente necessidade comprovada; n8n somente orquestração.
