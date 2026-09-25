# Módulo 05 — Live Command Center

## Objetivo
Aplicação visual Windows (`ObsLiveBot.CommandCenter`) que centraliza o ecossistema.

## Áreas previstas
Dashboard; OBS; Live; Chat; IA; Voz/TTS; Content Engine; Cortes; Automações/n8n; Configurações; Backup & Restore.

## Backend
C#/.NET é o núcleo. A UI não contém regras de negócio: envia Commands/Queries ao núcleo via contratos definidos. O projeto deve ser preparado para distribuição/instalação Windows (.exe/installer) em etapa própria.

## Status esperado
Exibir saúde/conectividade de OBS, n8n, integrações, workers Python/C++ quando existentes, jobs e backups, sem expor segredos.
