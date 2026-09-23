# Arquitetura

## Visão geral

```text
Plataformas de chat
        |
        v
Adaptadores de chat -----> Regras do OBS Live Bot -----> Fila / Anti-spam
                                   |                           |
                                   v                           v
                         Orquestração n8n              Adaptador de TTS
                                   |
                                   v
                         Adaptador OBS WebSocket
                                   |
                                   v
                              OBS Studio

                 Provedor de IA opcional
                           ^
                           |
                 Porta de IA substituível
```

## Papéis dos componentes

| Componente | Responsabilidade |
|---|---|
| OBS Studio | Executar a transmissão, cenas, fontes e áudio. |
| OBS WebSocket | Expor uma interface controlada para consultar estado e enviar comandos ao OBS. |
| n8n | Orquestrar eventos e integrações locais. |
| OBS Live Bot | Implementar regras, estados, filas, cooldowns e decisões da automação. |
| TTS | Converter texto em voz por meio de um adaptador substituível. |
| IA | Fornecer respostas opcionais quando regras locais não forem suficientes. |
| Docker | Isolar e executar a infraestrutura local, começando pelo n8n. |

## Limites arquiteturais

O núcleo do OBS Live Bot não deve depender diretamente de SDKs específicos. Cada integração deve implementar uma porta estável:

- `ChatConnector`: recebe eventos de uma plataforma de chat.
- `ObsGateway`: consulta estado e envia comandos via OBS WebSocket.
- `TtsProvider`: sintetiza áudio.
- `AiProvider`: gera respostas opcionais.
- `MessageRepository`: persiste estado e histórico necessário.
- `AutomationEngine`: coordena regras e timers, independentemente do n8n.

Os nomes representam contratos conceituais. Interfaces concretas serão definidas nas tasks de implementação.

## Substituição de componentes

- **Plataforma de chat:** adaptadores separados para YouTube, Twitch e Kick.
- **TTS:** provedor local ou remoto selecionado por configuração.
- **IA:** provedor desabilitável e substituível, sem lógica obrigatória no núcleo.
- **Banco:** repositório abstrato, permitindo iniciar com persistência simples e migrar depois.
- **Automação:** regras do núcleo independentes do n8n, permitindo substituir o orquestrador.

## Fluxo futuro esperado

1. Um conector normaliza uma mensagem recebida.
2. O núcleo aplica deduplicação, cooldown, anti-spam e regras locais.
3. A fila define a ordem de processamento.
4. O bot identifica boas-vindas, perguntas ou mensagens para TTS.
5. Quando aplicável, um timer aguarda resposta humana.
6. Uma resposta automática local é tentada primeiro.
7. IA pode ser consultada somente se habilitada e necessária.
8. O áudio é encaminhado ao adaptador de TTS e, depois, ao OBS.

## Segurança por padrão

- `OBS_CONFIG_ROOT` começa como somente leitura e não é montado no container.
- A integração operacional futura deve usar OBS WebSocket.
- O n8n do OBS Live Bot é exposto somente em `127.0.0.1:5679`, mantendo-se independente do n8n do GFM TruckHub na porta 5678.
- Senhas e tokens permanecem em `.env`, nunca no Compose ou no Git.
- Nenhum workflow funcional é incluído na fundação.
