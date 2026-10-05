# ADR-035 — Order Aggregate Persistence

- Status: Accepted
- Date: 2026-10-06
- Scope: Primeiro modelo de persistência real (TASK 1.0). Trading module, agregado `Order`.

## Context

O ADR-003 fixou PostgreSQL, EF Core e Npgsql, e deixou explícito que a primeira migration só
seria criada diante de um modelo legítimo. O agregado `Order` e seu `OrderFill` são esse modelo:
o Paper Trading precisa que ordens e execuções sobrevivam ao restart do processo.

Três fatos do domínio restringem a solução:

1. `Order` tem construtor privado e apenas fábricas de criação (`CreateMarket`/`CreateLimit`).
   Não existe fábrica de reidratação, e a criação deliberadamente rejeita estado que não seja
   `Created` com `LastUpdatedAt == CreatedAt`. Uma ordem parcialmente preenchida lida do banco
   **não** pode passar por essas fábricas sem fabricar história falsa.
2. `OrderFill` é um record imutável, identidade de execução única e global (`ExecutionId`), sem
   `OrderId` mutável e sem chave própria.
3. `Quantity` aceita até 28 casas decimais e `Order` prova conservação exata em unidades inteiras
   de 10⁻²⁸ via `BigInteger`. Qualquer armazenamento com escala menor que a do `decimal` do .NET
   quebraria a invariante que o domínio garante hoje.

## Decision

**Persistência como detalhe técnico; o Domínio não é alterado.** Nenhuma fábrica de reidratação,
construtor público, propriedade mutável ou atributo de framework é adicionado ao Domínio. O
mapeamento vive em `Infrastructure/Persistence/Configurations` e materializa o agregado por
backing field (`PropertyAccessMode.Field`), incluindo a coleção `_fills`.

**Armazenamento decimal exato.** Todo valor financeiro exato usa `numeric` sem precisão/escala
declaradas, com o tipo de coluna fixado explicitamente. Isso preserva o valor **e a escala** do
`decimal` do .NET, que é o que a conservação de quantidade exige. Precisão declarada como
`numeric(18,2)` ou similar é proibida para esses campos: truncaria ou arredondaria silenciosamente
um valor que o domínio considera exato. `Money` não é persistido nesta fase; quando for, a moeda
é uma coluna `text` separada e nunca um sufixo dentro do valor.

Sinais e taxas usam `numeric` pela mesma razão; apenas contagens inteiras usam `integer`/`bigint`.

**Enums como `text` com verificação de faixa nomeada**, não `integer`. O valor numérico de um enum
C# é um detalhe de compilação; reordenar membros corromperia dados históricos. Armazenar o nome
torna a leitura do banco auditável e compatível com a regra de que `Enum.IsDefined` guarda o domínio.

**Identidade e tempo.** `OrderId`, `InstrumentId` e `ExecutionId` usam `uuid`. `UtcInstant` usa
`timestamptz`, que é o tipo que carrega o instante absoluto que o domínio mantém (`ToUniversalTime`).
`timestamptz` resolve em microssegundos, enquanto um tick do .NET é 100 ns: o mapeamento **trunca
explicitamente para microssegundos antes de escrever**, em vez de permitir que o banco arredonde
sozinho. Sem isso, um instante como `13:30:00.1234567` voltaria como `.1234560` e a comparação de
igualdade de `UtcInstant` falharia depois de um reload — uma perda silenciosa. O truncamento é
idempotente: reler e regravar não desloca o valor.

**Módulo proprietário.** As tabelas de negócio vivem no schema `trading`, não em `public`. A
propriedade de dados por módulo é uma decisão do ADR-001 e o schema é o mecanismo que a torna
visível no banco. Isso não cria projeto por módulo nem garante isolamento de compilação. A tabela
`__EFMigrationsHistory` permanece em `public`: é metadado de infraestrutura do EF Core, não dado de
domínio, e o EF não herda o schema padrão do modelo para ela. Não será movida sem uma decisão
própria, porque movê-la afeta o histórico já aplicado.

**Reidratação não revalida invariantes.** A materialização por backing field reconstrói
`Status`, `LastUpdatedAt`, `RejectionReason` e os fills exatamente como persistidos, sem repetir
os guards do construtor. A consequência é aceita explicitamente: o banco é estado confiável e uma
linha corrompida poderia produzir um agregado inválido em memória. A alternativa — revalidar na
leitura — é pior: exigiria uma máquina de estados de replay no mapeamento e criaria uma segunda
autoridade sobre o ciclo de vida, justamente o que o ADR-034 proíbe. Detecção de corrupção será
tratada por verificação explícita, não por reexecução silenciosa de transições.

## Alternatives

**Fábrica de reidratação pública no Domínio.** Rejeitada: amplia a API pública do agregado e
permite construir uma `Order` com estado e timestamps arbitrários, contornando os guards de
criação. A necessidade é de persistência, não do domínio.

**Reconstruir via `CreateMarket`/`CreateLimit` e reaplicar fills.** Rejeitada: o construtor exige
`LastUpdatedAt == CreatedAt` e `Created`, então ordens aceitas, parciais ou terminais não são
representáveis sem inventar uma sequência de eventos. Fabricaria fatos comerciais a partir de
linhas de banco e tornaria a leitura dependente da ordem de aplicação.

**`jsonb` para fills ou para o agregado inteiro.** Rejeitada para os fills: perderia integridade
referencial e consulta por `ExecutionId`, que é a identidade autoritativa de deduplicação. O
agregado inteiro em JSON também dificultaria concorrência e evolução de schema.

**`real`/`double precision` para valores financeiros.** Rejeitada pelo ADR-002.

**`integer` escalado em vez de `numeric`.** Rejeitada: exigiria definir uma escala por campo e
reimplementar em SQL a aritmética que o domínio já faz, criando duas fontes de verdade sobre
conservação de quantidade.

## Consequences

- O Domínio permanece puro; a persistência não impõe atributos, construtores ou mutabilidade.
- O mapeamento depende de nomes de backing field. Renomear `_fills` ou os campos privados de
  `Order` quebra o mapeamento em runtime, então testes de round-trip passam a ser obrigatórios
  em toda alteração do agregado.
- Precisão temporal fica limitada a microssegundos na fronteira de persistência. O domínio continua
  aceitando ticks, mas um instante com resolução menor é normalizado ao ser gravado; consumidores
  que comparem instantes lidos do banco com instantes gerados em memória devem considerar isso.
- `numeric` sem precisão é ligeiramente mais lento que tipos de tamanho fixo e não permite
  otimizações de armazenamento; a exatidão tem precedência conforme o ADR-002.
- Índices e constraints precisam considerar que o banco não conhece as invariantes de `Order`;
  as garantias de ciclo de vida continuam exclusivas do domínio.
- Concorrência otimista, histórico durável de comandos e recuperação de crash não são resolvidos
  aqui e permanecem pendências do ADR-034.

## References

- ADR-001 — Modular Monolith (propriedade de dados por módulo).
- ADR-002 — Financial Numeric Types (exatidão decimal).
- ADR-003 — PostgreSQL + EF Core + Npgsql.
- ADR-032 — Minimal Order Domain Foundation.
- ADR-034 — Single-Order Execution Workflow.
- docs/architecture/postgresql-persistence.md.
