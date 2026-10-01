# ADR-003 — PostgreSQL + EF Core + Npgsql

- Status: Accepted
- Date: 2026-10-01.
- Scope: Fundação de persistência; sem entidades, schema de negócio ou migrations nesta fase.

## Context

O monólito modular precisa de persistência transacional para futuros estados de trading e dados associados. PostgreSQL 18 já está disponível na infraestrutura local aprovada. A primeira integração deve provar composição, conexão, health e tooling, preservando Domain independente de frameworks e sem antecipar modelos financeiros.

## Decision

Adotar PostgreSQL como source of truth transacional, EF Core como mecanismo de persistência e Npgsql como provider .NET/PostgreSQL. `VortexTradingDbContext` pertence à Infrastructure e tem lifetime scoped. Application e Domain não recebem dependências EF/Npgsql nem abstrações genéricas de repository ou Unit of Work.

EF Core é adequado por oferecer unidade transacional no DbContext, change tracking, queries LINQ e evolução explícita de schema por migrations. Npgsql fornece integração nativa com os tipos e recursos PostgreSQL. Versões estáveis compatíveis com .NET 10 são pinadas; detalhes operacionais estão em `docs/architecture/postgresql-persistence.md`.

Connection strings vêm de configuração externa; desenvolvimento usa User Secrets da API. Segredos não entram em appsettings, documentação, Git ou logs. Health geral/readiness verifica PostgreSQL pelo probe oficial de DbContext; liveness verifica o processo.

Redis permanece uma infraestrutura auxiliar para futuros casos de cache/estado efêmero/Pub/Sub. Não substitui PostgreSQL nem será source of truth de Orders, Executions, Positions ou Ledger; esta tarefa não instala cliente Redis.

## Consequences

- A persistência fica isolada na Infrastructure, mantendo as referências aprovadas e sem ciclos.
- Migrations tornam a evolução de schema explícita e revisável. A primeira será criada apenas quando houver um modelo legítimo; não haverá migration vazia ou aplicação automática no startup.
- EF Core não elimina a necessidade de definir precision/scale, concorrência, idempotência, transações e invariantes por caso de uso. ADR-002 continua orientando tipos financeiros.
- LINQ e change tracking exigem atenção a SQL gerado, tracking, projeções e custo das queries. Queries especializadas poderão utilizar SQL ou APIs do provider quando houver necessidade medida.
- DbContext não pode ser compartilhado entre operações concorrentes ou tratado como singleton. Background work deverá criar scopes apropriados quando for introduzido.
- O provider vincula mappings e traduções ao PostgreSQL. Upgrades de EF/Npgsql e major do banco exigem revisão de compatibilidade e schema.
- A configuração válida permite startup mesmo com banco indisponível; readiness sinaliza a falha e liveness permanece independente. Configuração ausente/malformada impede startup.

## Alternatives

**Npgsql direto ou Dapper:** permitem controle explícito de SQL, mas exigem manutenção manual de mappings e evolução de schema, sem change tracking. Podem complementar queries específicas futuramente; não oferecem benefício demonstrado para substituir a fundação EF Core agora.

**Outro ORM ou banco:** adicionaria uma escolha operacional nova sem necessidade comprovada. PostgreSQL já atende à persistência relacional e transacional pretendida e está aprovado no ambiente.

**Redis como persistência principal:** não atende à decisão de manter o estado transacional autoritativo no PostgreSQL. Sua disponibilidade não justifica acoplamento prematuro à aplicação.

## References

- [EF Core overview](https://learn.microsoft.com/en-us/ef/core/).
- [Npgsql EF Core provider](https://www.npgsql.org/efcore/).
- [Npgsql EF Core 10 e PostgreSQL 18](https://www.npgsql.org/efcore/release-notes/10.0.html).
- [PostgreSQL transaction isolation](https://www.postgresql.org/docs/18/transaction-iso.html).
