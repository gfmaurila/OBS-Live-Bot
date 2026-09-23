# Segurança

## Segredos

- `.env` e variantes locais são ignorados pelo Git.
- `.env.example` contém somente valores públicos ou campos vazios.
- `OBS_WEBSOCKET_PASSWORD` nunca deve receber valor no Git.
- Tokens de chats, TTS e IA deverão seguir a mesma regra.

## OBS

- `C:\Users\gfmau\AppData\Roaming\obs-studio` é tratado como somente leitura.
- O diretório não é montado no container.
- Integrações futuras devem preferir OBS WebSocket.
- Alterações em cenas, profiles, plugins, scripts, áudio, fontes e scene collections exigem escopo e autorização explícitos.

## Rede

- O n8n fica vinculado a `127.0.0.1` por padrão.
- `host.docker.internal` é reservado à comunicação do container com serviços no host.
- Nenhum serviço cloud é obrigatório.
- Exposição externa futura deve incluir autenticação, TLS e revisão de risco.

## Dados e logs

- Dados persistentes, logs e backups locais não são versionados.
- Logs não devem registrar senhas, tokens ou payloads sensíveis completos.
- Retenção e limpeza deverão ser configuráveis.
- Backups contendo credenciais devem ser protegidos fora do Git.

## Dependências

- Imagens e dependências deverão ser fixadas em versões revisadas antes de produção.
- Atualizações devem ser testadas antes de substituir versões operacionais.
- Nenhuma instalação automática no Windows é permitida sem autorização.

## IA opcional

- O bot deve operar com regras locais quando possível.
- Conteúdo enviado a um provedor de IA deve ser mínimo e explicitamente habilitado.
- Falha ou ausência de IA não deve interromper funções locais essenciais.

