# Módulo 04 — Content Engine

## Objetivo
Transformar gravações/lives em conteúdo reutilizável: transcrição, detecção de momentos, cortes, áudio contextual/divertido e metadata/copy adaptada a YouTube, TikTok e Instagram.

## Responsabilidades
C# coordena casos de uso, domínio, filas, persistência e contratos. Python executa workloads especializados de IA/mídia. C++ somente se houver gargalo/nativo comprovado. n8n pode orquestrar publicação e integrações, sem assumir o domínio.

## Fluxo por eventos
`VideoImportedDomainEvent` -> transcrição -> `TranscriptionCompletedDomainEvent` -> detecção de momentos -> criação de clip -> `ClipCreatedDomainEvent` -> geração de metadata/conteúdo.

A implementação ocorrerá somente em tasks futuras específicas.
