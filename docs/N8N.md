# n8n

## Papel

O n8n será o orquestrador local de eventos e integrações. As regras centrais do OBS Live Bot não devem ficar acopladas exclusivamente a workflows do n8n.

## Preparação atual

O `docker-compose.yml` define:

- imagem configurável por `N8N_IMAGE`;
- acesso local em `127.0.0.1:5679`;
- persistência em `./data/n8n`;
- diretórios locais para workflows e backups;
- healthcheck HTTP interno;
- rede dedicada;
- resolução de `host.docker.internal`;
- variáveis de conexão futura com OBS.

## Operação local

Iniciar:

```powershell
docker compose up -d
```

Verificar o container:

```powershell
docker compose ps
```

Visualizar logs:

```powershell
docker compose logs -f n8n
```

Parar sem apagar os dados persistentes:

```powershell
docker compose down
```

Acessar:

```text
http://localhost:5679
```

## Isolamento de portas

| Porta local | Projeto |
|---|---|
| 5678 | GFM TruckHub n8n |
| 5679 | OBS Live Bot n8n |

O mapeamento do OBS Live Bot é `127.0.0.1:5679:5678`. A porta interna padrão do n8n continua sendo 5678, mas não é publicada em interfaces externas.

## Persistência

| Caminho no host | Uso |
|---|---|
| `data/n8n` | Estado interno persistente do n8n. |
| `n8n/workflows` | Arquivos de workflows controlados pelo projeto. |
| `n8n/backup` | Backups locais, ignorados pelo Git. |

O bind mount `data/n8n:/home/node/.n8n` preserva banco local, configuração e estado quando o container é reiniciado ou recriado com `docker compose down` seguido de `docker compose up -d`.

## Segurança

- Não armazenar credenciais em workflows versionados.
- Não publicar a porta do n8n em interfaces externas por padrão.
- Não inserir segredos no `docker-compose.yml`.
- Usar `.env` local e mecanismos de credenciais do n8n.
- Revisar uma versão fixa da imagem antes de uso operacional.

## Smoke workflow

O workflow `OBS Live Bot - Smoke Test` deve ser criado pela interface oficial após a configuração inicial do proprietário, caso necessário. Não manipular diretamente o banco nem contornar autenticação para criá-lo.
