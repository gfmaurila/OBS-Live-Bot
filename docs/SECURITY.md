# Segurança

## Segredos de execução
`.env` e variantes locais ficam fora do Git. Nunca hardcode/versione tokens, senhas ou chaves. Logs não registram segredos.

## Backups OBS
O requisito funcional é preservar integralmente o ambiente OBS do usuário, inclusive credenciais que estejam armazenadas dentro de `OBS_CONFIG_ROOT` e sejam copiáveis. Portanto, backups podem conter segredos e devem ser tratados como artefatos sensíveis: criptografia/proteção, acesso restrito, integridade e exclusão do Git.

## OBS_CONFIG_ROOT
`C:\Users\gfmau\AppData\Roaming\obs-studio` é read-only em tasks normais. Backup pode ler/copiar. Restore/escrita exige task explícita, OBS fechado, validação, snapshot de segurança e rollback.

## Rede
n8n permanece local em `127.0.0.1:5679` por padrão. Exposição externa exige autenticação/TLS/revisão.

## Portabilidade
Credenciais protegidas por Windows/DPAPI/OAuth/provedor podem exigir reautenticação após formatação. O sistema deve relatar isso sem remover deliberadamente credenciais do backup.
