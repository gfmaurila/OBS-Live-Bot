# Segurança

## Segredos de execução
`.env` e variantes locais ficam fora do Git. Nunca hardcode/versione tokens, senhas ou chaves. Logs não registram segredos.

## Chave de criptografia do n8n

- `N8N_ENCRYPTION_KEY` deve ser gerada com CSPRNG e existir somente no `.env` local ignorado.
- `N8N_HMAC_SIGNATURE_SECRET`, `N8N_BINARY_DATA_SIGNING_SECRET` e `N8N_USER_MANAGEMENT_JWT_SECRET` também ficam no `.env` local. Elas desacoplam assinaturas operacionais da chave mestra e permitem substituir com segurança uma chave de instância comprometida.
- `data/n8n` é estado de runtime sensível: pode conter banco, owner, credenciais, logs e cópia local da chave. A árvore inteira fica fora do Git.
- Troca de chave exige backup protegido e, quando existirem credenciais, exportação descriptografada temporária pelo CLI oficial seguida de importação com a nova chave.
- Exports descriptografados são temporários, nunca versionados e devem ser removidos imediatamente após a validação.
- Uma chave presente no histórico Git é considerada comprometida e deve ser substituída; remover apenas o arquivo do `HEAD` não é suficiente.

## Backups OBS
O requisito funcional é preservar integralmente o ambiente OBS do usuário, inclusive credenciais que estejam armazenadas dentro de `OBS_CONFIG_ROOT` e sejam copiáveis. Portanto, backups podem conter segredos e devem ser tratados como artefatos sensíveis: criptografia/proteção, acesso restrito, integridade e exclusão do Git.

## Incidente do histórico n8n

O segredo do n8n anteriormente presente em `data/n8n/config` foi tratado como comprometido, rotacionado e removido do histórico Git. O histórico local e as refs ativas do remote foram reescritos em 2026-09-28. O estado de runtime `data/n8n/` e o `.env` permanecem locais e ignorados pelo Git.

Objetos antigos ainda podem ser retidos temporariamente pelo provedor de hospedagem em caches, backups, forks ou garbage collection internos; a reescrita garante que o histórico comprometido não seja alcançável pelas refs ativas auditadas. Nenhum valor antigo ou novo deve ser copiado para documentação.

Clones criados antes da sanitização não devem fazer pull ou merge do histórico antigo. Prefira um clone novo. Quando houver trabalho local legítimo a preservar, reaplique cuidadosamente apenas esses commits sobre o clone limpo, sem reintroduzir o histórico comprometido.

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
- Rotações geram senha com CSPRNG (mínimo de 32 bytes), gravam o valor somente no `.env` local e no arquivo WebSocket do OBS, e nunca exibem o valor.
- O backup pré-rotação do arquivo WebSocket é protegido com Windows DPAPI (`CurrentUser`) e permanece excluído do Git.
