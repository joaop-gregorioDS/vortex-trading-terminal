# Production deploy — VPS

Artefatos para hospedar o Vortex Trading Terminal na VPS da Vortex Software
(`vortexsoftware.tech`), preservando a stack aprovada: containers Linux, Docker Compose e Nginx.

## Estado real deste deploy

O app funciona de ponta a ponta, e **não é durável**: a sessão simulada vive em memória, então
reiniciar o container da API perde ordens, fills e posições. A persistência PostgreSQL existe para o
agregado `Order` (ADR-035), mas a sessão ainda não grava nem lê o banco. Isso está registrado como
consequência no ADR-039 e é a pendência mais valiosa do projeto.

O feed de mercado é **simulado e finito**: ele publica preços do gerador determinístico e para quando
a série termina. Não há dado real, fornecedor autorizado ou credencial envolvida.

## Topologia

```text
Internet
   ↓ 443 (TLS no host)
Nginx do host
   ├── /api/  → 127.0.0.1:8080   container api  (ASP.NET Core, ASP.NETCORE_ENVIRONMENT=Production)
   └── /      → 127.0.0.1:3000   container web  (Next.js standalone)
                                     ↓ rede vortex-trading
                                  container postgres (volume nomeado)
```

Os containers da aplicação publicam **apenas em 127.0.0.1**: quem fala com a internet é o Nginx do
host, que já existe para o domínio. Assim não há CORS a configurar, porque navegador e API ficam na
mesma origem, e o banco não fica exposto.

## Arquivos

| Arquivo | Papel |
| --- | --- |
| `deploy/Dockerfile.api` | build e runtime da API em container Linux não-root |
| `deploy/Dockerfile.web` | build e runtime do Next.js em modo standalone |
| `deploy/compose.yaml` | composição de produção: api, web e postgres |
| `deploy/.env.example` | placeholders das três variáveis obrigatórias |
| `deploy/nginx.conf.example` | server block pronto para colar no Nginx do host |

## Passos

Na VPS, com Docker Engine e Compose instalados:

```sh
# 1. código e configuração
git clone <repositório> /srv/vortex-trading
cd /srv/vortex-trading/deploy
cp .env.example .env

# 2. gere uma senha real para o PostgreSQL (não use o placeholder)
openssl rand -hex 32

# 3. edite .env e preencha POSTGRES_PASSWORD, REDIS_PASSWORD (se usar) e WEB_PORT/API_PORT

# 4. valide a composição sem imprimir segredos
docker compose --env-file .env -f compose.yaml config --quiet

# 5. suba
docker compose --env-file .env -f compose.yaml up -d --build

# 6. confirme saúde
curl -fsS http://127.0.0.1:8080/health/ready
curl -fsS http://127.0.0.1:8080/health/live
```

Depois copie `nginx.conf.example` para o Nginx do host, ajuste o nome do servidor e recarregue:

```sh
nginx -t && systemctl reload nginx
# TLS: certbot --nginx -d vortexsoftware.tech
```

## Verificação

```sh
curl -fsS https://vortexsoftware.tech/api/trading/market
curl -fsS https://vortexsoftware.tech/api/portfolio
```

O primeiro deve responder o preço simulado corrente; o segundo, o portfólio com valores em string
decimal.

## Operação

```sh
docker compose --env-file .env -f compose.yaml ps
docker compose --env-file .env -f compose.yaml logs -f api
docker compose --env-file .env -f compose.yaml down          # preserva volumes
```

`down -v` remove o volume do PostgreSQL e destrói os dados. O health check da composição usa
`/health/ready`, que verifica PostgreSQL: enquanto a sessão não usar o banco, um banco fora do ar
ainda derruba o readiness, o que é intencional — significa que a infraestrutura prevista está quebrada,
não que o app parou de funcionar.

## Limites que este deploy não resolve

- Sem durabilidade de sessão: reiniciar perde ordens e posições.
- Sem autenticação: qualquer visitante opera a mesma conta simulada.
- Sem multiusuário: uma sessão, uma conta.
- Sem observabilidade: apenas logs do container e os health checks.
- Sem Redis: previsto no ADR-003 para cache e estado efêmero, ainda não usado por caso de uso.

Essas lacunas são decisões pendentes, não detalhes de configuração, e estão registradas nos ADRs
correspondentes.
