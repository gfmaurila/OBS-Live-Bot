# Módulo 06 — Configuration, Backup & Restore

## Objetivo
Permitir reinstalar/formatar a máquina e reconstruir o ambiente OBS do usuário com o máximo de fidelidade possível.

## Fonte canônica do OBS
`C:\Users\gfmau\AppData\Roaming\obs-studio`

O backup OBS é **integral**: não selecionar apenas arquivos conhecidos. Copiar toda a árvore de `OBS_CONFIG_ROOT`, preservando profiles, scene collections, sources, filters, áudio/vídeo, encoder, streaming, gravação, hotkeys, docks, scripts, plugin configs, WebSocket e demais dados/configurações existentes, inclusive credenciais/tokens/senhas armazenados pelo OBS quando presentes e portáveis.

## Job obrigatório no fechamento do OBS
Um `ObsProcessMonitor`/BackgroundService deve detectar o encerramento de `obs64.exe`. Quando o processo encerrar, publicar o fato `ObsClosedDomainEvent` e iniciar, via Mediator, `CreateObsAutomaticBackupCommand`.

```text
obs64.exe encerra
 -> ObsClosedDomainEvent
 -> CreateObsAutomaticBackupCommand
 -> Mediator pipeline
 -> Handler
 -> snapshot integral OBS_CONFIG_ROOT
 -> manifest + SHA-256
 -> pacote protegido
 -> ObsBackupCreatedDomainEvent
```

### Regras do Job
- habilitado/configurável pelo Command Center;
- não executar dois backups simultâneos;
- aguardar liberação dos arquivos após fechamento do OBS;
- usar staging temporário e commit/rename atômico quando possível;
- validar manifest/checksums antes de marcar sucesso;
- nunca apagar o último backup válido;
- retenção configurável;
- registrar sucesso/falha sem registrar segredos;
- opcionalmente evitar novo pacote quando nenhum conteúdo OBS mudou, mantendo registro da verificação.

## Segurança do pacote
O requisito é **salvar também credenciais do ambiente OBS**, não removê-las do snapshot. Como isso pode incluir segredos, o artefato final deve suportar criptografia/proteção e nunca ser versionado no Git. Logs e manifest não devem revelar valores secretos.

Algumas sessões OAuth/credenciais podem ser vinculadas ao Windows, navegador, DPAPI, instalação ou provedor externo; nesses casos o restore deve detectar/relatar necessidade de reautenticação em vez de prometer portabilidade impossível.

## Restore
1. validar formato/versão;
2. validar checksums e integridade;
3. exigir OBS fechado;
4. criar backup de segurança da configuração atual;
5. restaurar a árvore completa;
6. validar resultado;
7. manter rollback disponível;
8. registrar `ObsBackupRestoredDomainEvent`.

## CQRS/Domain Events sugeridos
Commands: `CreateObsBackupCommand`, `CreateObsAutomaticBackupCommand`, `RestoreObsBackupCommand`, `DeleteExpiredBackupCommand`.
Queries: `GetBackupHistoryQuery`, `GetBackupStatusQuery`, `ValidateBackupQuery`.
Events: `ObsClosedDomainEvent`, `ObsBackupStartedDomainEvent`, `ObsBackupCreatedDomainEvent`, `ObsBackupFailedDomainEvent`, `ObsBackupRestoredDomainEvent`.

## Separação
O OBS Full Backup é específico ao ambiente OBS do usuário. Backup das configurações próprias do Command Center/n8n/Content Engine pode existir no mesmo módulo, mas como conjunto lógico separado.
