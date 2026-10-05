# PostgreSQL Persistence Foundation — TASK 0.5

> **Continuidade:** este documento descreve a fundação técnica (composição, conexão, health e
> tooling) e permanece válido. O primeiro modelo de negócio foi adicionado depois, com o agregado
> `Order` e a primeira migration; consulte [order-persistence.md](order-persistence.md) e o ADR-035.
> As afirmações abaixo sobre "zero DbSets" e "nenhuma migration" descrevem o estado da TASK 0.5.

Fundação sobre `0958c47`, sem entidades, repositories, migrations ou dados de negócio. Next.js e ASP.NET Core continuam no host; PostgreSQL e Redis continuam no Compose aprovado, sem alterações na infraestrutura Docker.

## Dependências e compatibilidade

Versões estáveis explícitas, verificadas nos metadados oficiais NuGet em 2026-10-01:

| Dependência | Versão | Local e finalidade |
| --- | --- | --- |
| `Microsoft.EntityFrameworkCore` | `10.0.12` | Infrastructure: DbContext e composição EF Core |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `10.0.3` | Infrastructure: provider PostgreSQL, trazendo `Npgsql 10.0.3` transitivamente |
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | `10.0.12` | Infrastructure: probe oficial de DbContext |
| `Microsoft.EntityFrameworkCore.Design` | `10.0.12` | API: startup project do tooling; `PrivateAssets=all`, sem compile assets ou exposição transitiva |
| `dotnet-ef` | `10.0.12` | Ferramenta local pinada em `.config/dotnet-tools.json` |

O provider 10.0.3 tem target `net10.0` e requer EF Core/Relational no intervalo `[10.0.4, 11.0.0)`; EF Core 10.0.12 satisfaz o contrato. O provider suporta PostgreSQL 18 e é configurado com `SetPostgresVersion(18, 0)`, alinhando traduções SQL à major usada no desenvolvimento. Nenhuma versão preview foi selecionada. SDK permanece `10.0.401`.

Fontes: [provider NuGet](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3), [EF Core NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.12), [Npgsql EF 10](https://www.npgsql.org/efcore/release-notes/10.0.html) e [probe oficial EF Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#entity-framework-core-dbcontext-probe).

## DbContext e composição

`src/VortexTrading.Infrastructure/Persistence/VortexTradingDbContext.cs` herda de `DbContext` e recebe `DbContextOptions<VortexTradingDbContext>`. Há zero DbSets e nenhuma configuração artificial de modelo. Não há `OnModelCreating`, `OnConfiguring`, factory manual, repository ou Unit of Work custom.

`AddInfrastructure(IConfiguration)` obtém `ConnectionStrings:PostgreSQL`, rejeita valor ausente/vazio/whitespace e valida a sintaxe com `NpgsqlConnectionStringBuilder`. Erros de configuração são claros, sem incluir o valor ou preservar uma inner exception que possa revelá-lo. Essas verificações ocorrem durante composição, antes de atender HTTP. A disponibilidade real do banco é verificada pelo health check, não durante startup.

`AddDbContext` registra o contexto e suas options com lifetime padrão scoped: uma instância por scope de request. Não há singleton, pooling de DbContext ou política de retries customizada. Defaults do provider são preservados. `EnableSensitiveDataLogging` e detalhes adicionais de erros PostgreSQL não são habilitados.

Referências de projeto preservadas: Domain sem dependências; Application → Domain; Infrastructure → Application + Domain; API → Application + Infrastructure. EF Core e Npgsql permanecem na Infrastructure. A API tem apenas o pacote Design direto para o tooling. Redis continua completamente desacoplado do código, sem cliente instalado.

## Connection string e User Secrets

A chave esperada é `ConnectionStrings:PostgreSQL`. Não existe valor ou placeholder funcional em appsettings. A API possui `UserSecretsId` versionável; a connection string local fica no armazenamento User Secrets do usuário, fora do repositório, carregado automaticamente no ambiente Development pelo builder padrão do ASP.NET Core.

Para este ambiente local, a API conecta a `127.0.0.1:5432`, database/user `vortex_trading`, usando a credencial local do PostgreSQL aprovado. `infra/.env` continua ignorado; seu conteúdo não é carregado pela API nem copiado para arquivos versionados. A configuração local realizada nesta tarefa utilizou stdin do comando User Secrets, sem segredo nos argumentos ou saída.

Para configurar outra máquina, forneça a connection string local por entrada mascarada no PowerShell 7, a partir da raiz, sem imprimi-la:

```powershell
$localConnectionString = Read-Host 'Local PostgreSQL connection string' -MaskInput
$secretPayload = @{ 'ConnectionStrings:PostgreSQL' = $localConnectionString } | ConvertTo-Json -Compress
$secretPayload | dotnet user-secrets set --project src/VortexTrading.Api
Remove-Variable localConnectionString, secretPayload
```

User Secrets é um mecanismo de desenvolvimento, não um cofre criptografado. Não execute `user-secrets list` em relatórios ou logs compartilhados. Fora de Development, um provedor externo deverá fornecer a mesma chave, por exemplo a variável de ambiente `ConnectionStrings__PostgreSQL`; a política de produção será definida separadamente. Referência: [User Secrets oficial](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).

## Health checks

Infrastructure registra `AddDbContextCheck<VortexTradingDbContext>` com nome `postgresql` e tag `ready`. O pacote oficial é necessário para integrar EF ao Health Checks nativo e resolver o contexto em um scope. O probe usa `Database.CanConnectAsync`: testa conexão sem criar banco, schema, tabelas ou dados. Não há probe Redis.

| Endpoint GET | Semântica | Banco disponível | Banco indisponível |
| --- | --- | --- | --- |
| `/health` | Geral, inclui PostgreSQL | `200 Healthy` | `503 Unhealthy` |
| `/health/live` | Processo, exclui probes de dependências | `200 Healthy` | `200 Healthy` |
| `/health/ready` | Dependências com tag `ready`: PostgreSQL | `200 Healthy` | `503 Unhealthy` |

As respostas usam o writer padrão, `text/plain`, somente com o status agregado. Não expõem host, username, connection string, senha, stack trace ou mensagens internas. Todos os endpoints são restritos a GET. A compatibilidade HTTP de `/health` é mantida, com semântica ampliada para a dependência. Liveness pressupõe composição válida: configuração ausente impede o próprio startup.

## Tooling e migrations

`dotnet-ef` não estava disponível; foi adotado manifest local, sem instalar ou atualizar ferramenta global. Execute na raiz:

```powershell
dotnet tool restore
dotnet restore VortexTrading.slnx
dotnet build VortexTrading.slnx --no-restore
dotnet ef --version
dotnet ef dbcontext list --project src/VortexTrading.Infrastructure --startup-project src/VortexTrading.Api --no-build -- --environment Development
dotnet ef dbcontext info --project src/VortexTrading.Infrastructure --startup-project src/VortexTrading.Api --no-build -- --environment Development
dotnet run --project src/VortexTrading.Api --no-build --no-launch-profile -- --environment Development --urls http://127.0.0.1:5187
```

Infrastructure é o target project do DbContext e de futuras migrations; API é o startup project e contém Design com assets privados. O tooling consegue descobrir e criar o contexto via composição normal, sem `IDesignTimeDbContextFactory`. `dbcontext info` apresenta nome do banco/data source: não substitui um relatório de health público.

Nenhuma migration vazia foi criada. Não há `EnsureCreated`, `EnsureDeleted`, `Migrate`, seed ou migration automática no startup. A primeira migration deverá acompanhar uma necessidade legítima de modelo, após revisão das invariantes e mappings. Referências: [EF CLI](https://learn.microsoft.com/en-us/ef/core/cli/dotnet) e [criação design-time](https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation).

## Validação da TASK 0.5

Restore/build: zero errors e warnings; frontend lint/build aprovados sem alterar arquivos frontend. Tooling descobriu e criou o contexto com provider Npgsql e target PostgreSQL 18. Os três health endpoints responderam `200 Healthy` com o banco disponível.

Configuração ausente e chave de connection string inválida impediram startup com mensagens não sensíveis. Uma instância temporária com porta PostgreSQL local indisponível retornou `503 Unhealthy` em geral/readiness e manteve liveness `200 Healthy`, sem parar containers. A configuração normal permaneceu saudável e ambas as instâncias de teste foram encerradas. Respostas e logs revisados não continham credenciais.

Consulta de catálogo confirmou zero tabelas de aplicação e zero schemas customizados, preservando os schemas padrão PostgreSQL. Nenhuma migration, entidade ou dado de negócio foi criado. PostgreSQL/Redis permaneceram healthy. Auditoria NuGet com dependências transitivas não identificou vulnerabilidades conhecidas nas fontes consultadas.
