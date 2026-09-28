# Política de custo e infraestrutura

## Princípios permanentes

O GFM StudioOS adota:

- `LOCAL FIRST`;
- `ZERO INFRASTRUCTURE COST`;
- `NO PREMATURE INFRASTRUCTURE`;
- `NO PREMATURE MICROSERVICES`;
- o componente mais simples que satisfaça o requisito.

Serviços externos pagos e infraestrutura cloud não são adicionados sem necessidade explícita. Docker e Docker Compose continuam permitidos para componentes locais, mas cada novo container precisa resolver um problema concreto.

## Persistência

Banco de dados não faz parte da stack obrigatória. EF Core permanece disponível, mas não autoriza criar DbContext, migrations, repositories ou provider preventivamente.

Quando persistência relacional for realmente necessária:

- avaliar SQLite primeiro para uso local simples;
- avaliar PostgreSQL para cenários maiores ou multiusuário;
- considerar MySQL ou outro provider quando os requisitos justificarem;
- manter Application e Domain desacoplados do provider concreto.

Persistência documental com MongoDB somente deve ser adotada diante de uma necessidade documental clara.

## Estado, cache e mensageria

Enquanto o StudioOS operar localmente em instância única, preferir estruturas thread-safe em memória do .NET.

- Redis somente para estado/cache/deduplicação/rate limiting compartilhados, pub/sub ou múltiplas instâncias.
- RabbitMQ somente para filas duráveis, consumers independentes, retries assíncronos ou workloads que não possam ser perdidos.
- Kafka somente para alto volume, event streaming, múltiplos consumers, retenção e replay distribuído.
- MediatR permanece o mecanismo de commands, queries e notifications dentro do processo.

## n8n

O `obs-live-bot-n8n` permanece local via Docker, sem n8n Cloud. Ele orquestra integrações e não substitui Domain, Application, MediatR nem a fonte de verdade do sistema.

## Checklist de adoção

Antes de adicionar infraestrutura, documentar:

1. o problema concreto;
2. por que a solução nativa, local ou em memória não resolve;
3. se persistência ou durabilidade são necessárias;
4. se o estado precisa ser compartilhado entre processos;
5. se há necessidade de escala horizontal;
6. se existem múltiplos consumers independentes;
7. se replay é necessário;
8. o custo operacional introduzido;
9. a alternativa local e gratuita mais simples considerada.

Sem justificativa técnica concreta, a dependência não deve ser adicionada.

## OBS-LIVE-BOT-04

Live Chat Ingestion usa `LiveChatBuffer` em memória, thread-safe e limitado, com capacidade padrão 500 ou valor configurável equivalente. A perda do histórico ao reiniciar a aplicação é comportamento esperado nesta etapa.

Task 04 não usa banco, EF Core, Redis, RabbitMQ, Kafka ou MongoDB. Persistência de chat exige decisão futura explícita.
