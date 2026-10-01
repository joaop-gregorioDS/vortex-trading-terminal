# Development Infrastructure — TASK 0.4

Next.js (`apps/web`) e ASP.NET Core (`VortexTrading.Api`) continuam executando diretamente no host Windows. Docker Desktop no contexto `desktop-linux` executa exclusivamente PostgreSQL e Redis. A API ainda não se conecta a esses serviços e não recebe connection strings nesta etapa.

## Imagens e acesso local

Tags oficiais estáveis explícitas, verificadas em 2026-10-01 antes da seleção:

| Serviço | Imagem | Binding no host | Porta no container |
| --- | --- | --- | --- |
| PostgreSQL | `postgres:18.6-bookworm` | `127.0.0.1:5432` | `5432` |
| Redis | `redis:8.10.2-trixie` | `127.0.0.1:6379` | `6379` |

As portas foram escolhidas após inventário das publicações Docker, listeners TCP e faixas reservadas do Windows. Não havia conflito em 5432/6379. A publicação explícita em localhost evita exposição pela interface de rede do host. Não há TLS nesta infraestrutura local.

Fontes: [imagem oficial PostgreSQL](https://hub.docker.com/_/postgres), [imagem oficial Redis](https://hub.docker.com/_/redis) e [configuração oficial Redis](https://redis.io/docs/latest/operate/oss_and_stack/management/config/). Não se utiliza `latest`; upgrades exigem revisão explícita das tags e da compatibilidade dos dados.

## Isolamento e persistência

`infra/compose.yaml` declara o projeto `vortex-trading`, somente os serviços `postgres` e `redis`, e a rede bridge exclusiva `vortex-trading-network`. Nenhum recurso externo é referenciado. Compose gerencia os nomes dos containers e os volumes:

| Volume | Destino | Uso |
| --- | --- | --- |
| `vortex-trading_postgres-data` | `/var/lib/postgresql` | Cluster PostgreSQL; `PGDATA` oficial da major 18: `/var/lib/postgresql/18/docker` |
| `vortex-trading_redis-data` | `/data` | AOF Redis habilitado com `appendonly yes`; demais defaults preservados |

O caminho do PostgreSQL segue a mudança oficial da imagem na major 18; não se monta `/var/lib/postgresql/data` para essa major. Dados não usam bind mounts. Redis é infraestrutura auxiliar futura para cache, estado efêmero ou Pub/Sub quando houver caso de uso; não é source of truth de Orders, Executions, Positions ou Ledger.

Ambos usam `restart: "no"`: inicialização e parada ficam explícitas no desenvolvimento, sem loops automáticos ou retorno inesperado após reinício do Docker. Não há quotas ou tuning de banco/cache.

## Credenciais locais e autenticação

`infra/.env.example` contém apenas placeholders e é versionável. `infra/.env` é ignorado pela política existente do repositório, contém somente credenciais locais descartáveis e nunca deve ser staged, publicado ou compartilhado. As duas variáveis são obrigatórias, sem senha padrão no Compose.

PostgreSQL cria o banco e usuário `vortex_trading`. Conexões TCP pela rede Docker, incluindo acesso pela porta publicada no host, usam autenticação por senha SCRAM-SHA-256 por default da imagem. O usuário de bootstrap tem privilégios administrativos: a política de roles da aplicação será definida posteriormente. A senha é aplicada na inicialização de um volume vazio; editar `.env` não altera automaticamente a senha de um cluster existente. Preserve as credenciais junto aos dados locais.

Redis exige `REDIS_PASSWORD`. O comando passa uma configuração mínima por stdin ao `redis-server`, sem senha nos argumentos do processo e sem arquivo de configuração com segredo. Executa como usuário `redis`. A senha deve ser hexadecimal para manter a configuração por stdin simples e segura; caracteres fora desse formato são rejeitados antes da inicialização. O healthcheck e os testes usam `REDISCLI_AUTH`, evitando `redis-cli -a`. Referências: [stdin no servidor Redis](https://github.com/redis/redis/blob/8.10.2/src/server.c) e [autenticação no Redis CLI](https://redis.io/docs/latest/develop/tools/cli/).

Gere senhas independentes com 32 bytes aleatórios (64 caracteres hexadecimais), sem imprimi-las. Execute na raiz do repositório com PowerShell 7; o comando recusa sobrescrever `.env` existente:

```powershell
$envPath = Join-Path (Get-Location) 'infra/.env'
if (Test-Path -LiteralPath $envPath) { throw 'infra/.env already exists' }
$postgresPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$redisPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$content = "POSTGRES_PASSWORD=$postgresPassword`nREDIS_PASSWORD=$redisPassword`n"
[IO.File]::WriteAllText($envPath, $content, [Text.UTF8Encoding]::new($false))
Remove-Variable postgresPassword, redisPassword, content
git check-ignore infra/.env
```

Não há Docker Secrets nesta etapa. Quem administra o Docker local consegue consultar o ambiente dos containers; esta política é apenas para desenvolvimento. Não copie saídas expandidas de `compose config` ou `docker inspect` contendo credenciais para relatórios.

## Operação

Execute os comandos seguintes dentro de `infra/`. Valide sem imprimir a configuração expandida:

```powershell
docker compose --env-file .env -f compose.yaml config --quiet
docker compose --env-file .env -f compose.yaml up -d
docker compose --env-file .env -f compose.yaml ps
```

Healthchecks a cada 10 segundos, timeout de 5 segundos e 5 retries: PostgreSQL usa `pg_isready` via TCP com banco/usuário configurados e start period de 30 segundos; Redis verifica `PING` autenticado e resposta exata `PONG`, com start period de 10 segundos. `pg_isready` verifica disponibilidade; a consulta abaixo valida separadamente a autenticação.

Verificações funcionais sem gravar dados de aplicação ou revelar senhas (aspas simples preservam as referências para expansão dentro do container). PostgreSQL conecta ao hostname do serviço na rede Docker para exercitar a autenticação SCRAM, evitando o `trust` local de bootstrap da imagem:

```powershell
docker compose --env-file .env -f compose.yaml exec -T postgres sh -ec 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h postgres -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -c "SELECT 1;"'
docker compose --env-file .env -f compose.yaml exec -T redis sh -ec 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli -e --raw PING'
```

Para parar apenas esta infraestrutura e preservar os volumes:

```powershell
docker compose --env-file .env -f compose.yaml down
```

`down` remove os containers e a rede deste projeto, preservando os volumes nomeados. A opção `-v` remove volumes e destrói os dados locais; não deve ser usada rotineiramente. `up -d` recria os containers/rede usando os mesmos volumes. Não utilize comandos globais de limpeza do Docker para operar este projeto.
