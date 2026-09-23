# AGENTS.md

## Escopo

Este repositório contém o projeto independente OBS Live Bot. Não reutilizar acoplamentos, nomes, credenciais ou configurações do GFM TruckHub.

## Regras obrigatórias

- Tratar `OBS_CONFIG_ROOT` como somente leitura até uma tarefa autorizar explicitamente outro comportamento.
- Nunca modificar cenas, profiles, plugins, scripts, configurações, áudio, fontes ou scene collections do OBS.
- Não armazenar senhas, tokens, chaves ou outros segredos no Git.
- Não instalar software no Windows sem autorização explícita.
- Não iniciar, encerrar ou reconfigurar o OBS Studio sem autorização explícita.
- Preferir regras locais determinísticas; IA deve permanecer opcional.
- Manter conectores de chat, TTS, IA, banco e automação atrás de contratos substituíveis.
- Não criar workflows funcionais do n8n antes da tarefa correspondente.

## Desenvolvimento

- Implementar somente a task solicitada.
- Atualizar a documentação relacionada a mudanças arquiteturais.
- Validar configurações antes de executar serviços.
- Usar `.env` para valores locais e segredos; versionar apenas `.env.example` sem valores sensíveis.
- Preservar dados persistentes locais em diretórios ignorados pelo Git.

