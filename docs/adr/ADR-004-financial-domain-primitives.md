# ADR-004 — Financial Domain Primitives

- Status: Accepted
- Date: 2026-10-01.
- Scope: Primitives de Markets/Trading, sem entidades ou persistência.

## Context

A infraestrutura e persistência estão aprovadas, mas ainda não existe modelo de negócio. Os próximos módulos precisam distinguir identidade textual, moeda, valor monetário, preço unitário e quantidade para evitar combinações semanticamente inválidas de strings e decimals. A fundação deve proteger regras justificáveis sem antecipar estrutura de agregados ou convenções de mercado.

## Decision

Introduzir `Symbol`, `Currency`, `Money`, `Price` e `Quantity` no Domain, sem dependências externas, com `DomainException` comum para violação de invariantes.

Symbol e Currency rejeitam null/vazio/whitespace e normalizam com trim e uppercase invariant. Symbol não conhece exchange ou formato específico; Currency não impõe catálogo ISO ou três caracteres, preservando espaço para unidades digitais. A presença de um código não prova sua existência em um mercado ou registro externo.

Money associa `decimal Amount` a Currency válida, permitindo valores negativos, zero e positivos. Soma/subtração exigem moedas iguais e retornam novo valor; mismatch de moeda ou operandos null falha explicitamente. Price e Quantity usam decimal e exigem valores não negativos; zero e quantidades fracionárias são permitidos nesta foundation.

As primitives usam records selados de referência com propriedades get-only, igualdade/hash por valor e construção validada. Não há setters/init públicos ou conversões implícitas. `ToString` é determinístico e usa cultura invariant para números.

Criação preserva o decimal recebido, sem rounding/truncamento ou escala fixa. Aritmética monetária usa as regras e limites do decimal .NET, com overflow explícito. A política numérica permanece alinhada ao ADR-002; decimal não equivale a precisão arbitrária.

## Consequences

- O compilador distingue Money, Price e Quantity mesmo tendo backing numérico comum.
- As invariantes impedem identidade ausente, Money sem moeda e preços/quantidades negativos na construção.
- Operações monetárias não misturam moedas nem presumem câmbio.
- A igualdade não depende de referências ou cultura corrente. Testes determinísticos cobrem as fronteiras e preservação de escala.
- Records de referência têm custo de alocação, mas evitam valores struct default inválidos e simplificam as invariantes nesta etapa.
- Symbol precisa de contexto adicional para identidade entre mercados; Currency não funciona como registro completo de moedas válidas.
- Defaults do decimal impõem limites de precisão/overflow. Regras econômicas de arredondamento continuam explícitas e futuras.

## Deferred Decisions

Adiar tick size, lot/step size, positividade de quantidade de Order, Currency do instrumento, FX conversion, precisão/scale PostgreSQL e mappings/converters de fronteira. Não criar Entity base, repositories, Unit of Work, agregados, DbSets ou migrations sem um modelo real.

Adiar primitives temporais até Market Data, exigindo então semântica temporal explícita e determinística. Redis não participa destas decisões de domínio.

## Alternatives

Strings/decimals diretamente em todo o modelo não impediriam misturar valores de semântica distinta ou moedas incompatíveis. Wrappers mutáveis permitiriam contornar validação. Structs exigiriam uma política para seus valores default; records de referência com construção explícita resolvem a necessidade atual sem uma infraestrutura manual de equality.

## References

- ADR-002 — Financial Numeric Types.
- [C# records](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record).
- [C# decimal](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/floating-point-numeric-types).
