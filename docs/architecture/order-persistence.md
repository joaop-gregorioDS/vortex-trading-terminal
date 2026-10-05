# Order Persistence — TASK 1.0

Primeiro modelo de persistência real do projeto, sobre o checkpoint `6083abc`. Materializa o
agregado `Order` em PostgreSQL e cria a primeira migration. Nenhuma outra camada mudou: o paper
exchange, o coordinator e as APIs de ordem continuam pendentes.

## Escopo e propriedade

`Order` e `OrderFill` são os únicos tipos persistidos. Ambos pertencem ao módulo Trading e vivem no
schema `trading`. Tabelas:

| Tabela | Chave primária | Observação |
| --- | --- | --- |
| `trading.orders` | `Id` (`uuid`) | Chave fornecida pelo chamador; sem geração no banco |
| `trading.order_fills` | `ExecutionId` (`uuid`) | Uma execução não pode ser armazenada duas vezes, mesmo em ordens distintas |

`order_fills.OrderId` é chave estrangeira para `orders.Id` com `ON DELETE CASCADE`: o fill não existe
fora do agregado que o contém. A exclusão de ordens não é um fluxo do produto hoje; o cascade existe
para manter a integridade do agregado, não para autorizar remoção.

`__EFMigrationsHistory` permanece em `public`, por ser metadado do EF Core e não dado de domínio.

## Valores exatos

Todo valor financeiro exato é `numeric` **sem precisão ou escala declaradas**. O banco preserva o
valor e a escala do `decimal` do .NET, e um teste de round-trip grava `1.000000000000000000000000001`
e `0.000000000000000000000000003` (27 casas) e confirma igualdade exata na leitura. Um
`numeric(18,2)` ou qualquer escala fixa teria arredondado esses valores em silêncio, quebrando a
conservação de quantidade que `Order` prova em unidades inteiras de 10⁻²⁸.

Campos derivados — `FilledQuantity`, `RemainingQuantity` e `CompletedAt` — **não** são armazenados.
São consequência dos fills e do ciclo de vida; armazená-los criaria uma segunda autoridade sobre a
conservação de quantidade.

## Enums, identidades e tempo

Enums são armazenados por **nome** (`varchar`), não por ordinal: o valor numérico de um enum C# é
detalhe de compilação e reordenar membros corromperia linhas históricas. Um teste lê a coluna crua e
confirma `"Rejected"` e `"ExecutionProviderRejected"` como texto.

`OrderId`, `InstrumentId` e `ExecutionId` são `uuid`. Instantes são `timestamptz`. Como
`timestamptz` resolve em microssegundos e um tick do .NET é 100 ns, o mapeamento trunca
explicitamente para microssegundos antes de escrever. Sem isso, `13:30:00.1234567` voltaria como
`.1234560` e a igualdade de `UtcInstant` falharia após um reload. O truncamento é idempotente e
coberto por dois testes: um instante já alinhado sobrevive intacto, e um instante com ticks extras é
normalizado de forma estável.

## Reidratação sem alterar o Domínio

`Order` tem construtor privado e apenas fábricas de criação. Nenhuma fábrica de reidratação,
construtor público, propriedade mutável ou atributo de framework foi adicionado ao Domínio. O
mapeamento materializa por backing field, incluindo a coleção `_fills`.

A consequência é aceita explicitamente no ADR-035: a leitura **não** revalida as invariantes. Uma
linha corrompida poderia produzir um agregado inválido em memória. Revalidar na leitura exigiria uma
máquina de estados de replay no mapeamento e criaria uma segunda autoridade sobre o ciclo de vida,
justamente o que o ADR-034 proíbe. Detecção de corrupção será verificação explícita, não reexecução
silenciosa de transições.

Como o mapeamento depende de nomes de backing field, testes de round-trip são obrigatórios em
qualquer alteração do agregado: renomear `_fills` quebraria a persistência de forma silenciosa até o
primeiro `SaveChanges`.

## Concorrência e limites

Não há concorrência otimista, rowversion, histórico durável de comandos, recuperação de crash,
reconciliação ou idempotência de comandos. O ADR-034 já registra essas pendências para execução
externa; esta fase não as resolve e não promete exatamente-uma-vez.

O contexto continua com lifetime scoped e sem `EnsureCreated`, `Migrate` automático no startup ou
seed. A migration é aplicada explicitamente; os testes a aplicam no ambiente de desenvolvimento.

## Validação

Onze testes em `tests/backend/VortexTrading.Infrastructure.Tests/Persistence` cobrem: round-trip de
ordem a mercado sem limite, ordem aceita e parcialmente preenchida com quantidades derivadas,
exatidão decimal com 27 casas, rejeição de `ExecutionId` duplicado pelo banco, nomes de enum na
coluna crua, todos os motivos de rejeição, todos os `OrderStatus`, precisão sub-segundo, truncamento
sub-microssegundo idempotente, schema e `numeric` sem precisão declarada, e remoção de fills em
cascata.

Os testes **ignoram** quando não há connection string configurada, para que o CI sem banco continue
verde; um banco configurado mas inacessível é erro, não skip. O projeto de teste usa o mesmo
`UserSecretsId` da API e resolve a mesma connection string de desenvolvimento.

Verificação adicional no banco após a execução: schema `trading` com as duas tabelas, `numeric` sem
precision/scale, `timestamp with time zone`, enums como `character varying`, uma migration aplicada e
zero linhas residuais após os testes.

## Comandos

Da raiz, com a infraestrutura local no ar:

```sh
dotnet tool restore
dotnet ef migrations add <Name> --project src/VortexTrading.Infrastructure --startup-project src/VortexTrading.Api --no-build
dotnet ef database update --project src/VortexTrading.Infrastructure --startup-project src/VortexTrading.Api --no-build
dotnet test tests/backend/VortexTrading.Infrastructure.Tests
```

`ASPNETCORE_ENVIRONMENT=Development` é necessário para que o startup project resolva a connection
string via User Secrets.

## Referências

- ADR-035 — Order Aggregate Persistence.
- ADR-002 — Financial Numeric Types.
- ADR-003 — PostgreSQL + EF Core + Npgsql.
- [docs/architecture/postgresql-persistence.md](postgresql-persistence.md).
