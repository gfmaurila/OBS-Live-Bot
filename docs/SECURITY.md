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

## OBS WebSocket

- `OBS_WEBSOCKET_PASSWORD` é opcional e somente pode vir do ambiente local/`.env` ignorado.
- Senha vazia é válida quando o `Hello` do OBS WebSocket não anuncia autenticação.
- Se o servidor anunciar um desafio, o cliente usa o algoritmo challenge/salt do protocolo 5.x; sem senha, falha de forma segura.
- Logs registram somente códigos operacionais e tipos de erro, nunca senha, challenge, salt ou resposta de autenticação.
- `.dockerignore` exclui `.env`, dados do n8n, logs e backups do contexto da imagem.
- Respostas de `/health` e `/api/obs/status` não possuem campos de credencial.
