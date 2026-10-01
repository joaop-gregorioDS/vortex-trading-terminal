# ADR-002 — Financial Numeric Types

- Status: Accepted
- Date: 2026-10-01
- Scope: Política numérica inicial, sem cálculos ou persistência implementados.

## Context

Valores financeiros precisam de precisão consistente entre cálculo, armazenamento e transporte. Tipos binários aproximados não devem ser usados indiscriminadamente para representar dinheiro ou estado financeiro autoritativo.

## Decision

Usar C# `decimal` para dinheiro, preços, saldos, taxas, P&L e quantidades fracionárias quando necessário. Moeda, unidade, tick size, lot size, escala e modo de arredondamento serão explícitos conforme o instrumento e a operação; não assumiremos duas casas para todos os ativos.

Permitir `double` para indicadores, estatística e cálculos aproximados com tolerâncias documentadas. Conversões para valores financeiros serão explícitas, controladas e validadas; resultados aproximados não substituem a contabilidade exata.

Usar `int` ou `long` para contagens e quantidades inteiras conforme a faixa necessária. Inteiros escalados poderão representar valores exatos após definição explícita de unidade, escala e limites de overflow; otimizações dependerão de medição.

PostgreSQL utilizará futuramente `numeric(p,s)` quando precisão decimal exata for necessária. Precisão e escala serão escolhidas por campo e compatíveis com os limites de `decimal`; não haverá uma escala universal aplicada sem análise.

## Transport and rounding

Valores financeiros exatos serão transportados como strings decimais ou inteiros escalados com escala explícita. O `number` de JavaScript poderá servir à visualização quando a aproximação for aceitável; não será fonte autoritativa de saldo ou execução.

Arredondamentos serão realizados nas fronteiras definidas pela regra de negócio. Não haverá conversões silenciosas entre `decimal` e `double`. Divisão, overflow, limites de precisão e valores não finitos exigirão tratamento explícito conforme o tipo utilizado.

## Consequences

A precisão decimal traz custo de CPU e memória e não elimina limites de representação ou erros de regra. A implementação futura deverá verificar arredondamento, unidades, serialização e consistência dos resultados. Otimizações não poderão alterar a semântica financeira.

## References

- [C# floating-point numeric types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/floating-point-numeric-types).
- [PostgreSQL numeric types](https://www.postgresql.org/docs/current/datatype-numeric.html).
