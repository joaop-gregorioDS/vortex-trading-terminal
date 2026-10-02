# ADR-005 — Market Instrument Identity

- Status: Accepted
- Date: 2026-10-01.
- Scope: Identidade e composição de Instrument no Domain, sem persistência ou Market Data.

## Context

Symbol isolado não identifica inequivocamente um instrumento em uma plataforma multi-mercado: o mesmo texto pode ser usado em venues diferentes. O modelo precisa de identidade interna estável e contexto comercial explícito antes de Trading ou Market Data referenciá-lo. As primitives aprovadas protegem valores locais, mas os increments pertencem ao instrumento.

## Decision

Usar `InstrumentId` baseado em Guid não vazio como identidade interna. Permitir construção explícita/`From(Guid)` para reconstrução e `New()` para geração local, sem depender de integer identity do banco. Representação textual usa formato Guid `D`.

Tratar `Symbol + Venue` como identidade natural/comercial relevante. Não declarar unicidade global nem aplicar essa restrição dentro de um objeto isolado. Catálogo, aliases e necessidades adicionais de contexto exigirão requisitos próprios.

Modelar Instrument diretamente como classe selada imutável, sem base Entity, com Id, Symbol, Venue, AssetClass, Currency, PriceIncrement e QuantityIncrement. Igualdade e hashing usam exclusivamente InstrumentId; metadata não participa da identidade.

Venue é um value object textual normalizado por `Trim().ToUpperInvariant()`, sem catálogo ou formato de exchange. AssetClass é enum fechado com Other, Equity, ETF, Future, Option, Forex, Crypto, Index e FixedIncome. Other tem valor zero; valores numéricos indefinidos são rejeitados na construção de Instrument.

Exigir todos os componentes e increments estritamente positivos. Price e Quantity mantêm sua semântica global de valores não negativos. Alinhamento de preço/quantidade usa resto decimal igual a zero, aceita zero, rejeita entradas ausentes e nunca modifica o valor. Não arredondar, converter para double ou impor escala fixa.

## Consequences

- Mesmo Symbol em venues diferentes pode representar entidades distintas.
- InstrumentId pode ser reconstruído e referenciado independentemente de alterações futuras de metadata.
- Mesmo Id com propriedades diferentes mantém a igualdade conceitual; a fronteira futura de catálogo deverá impedir metadata inconsistente quando necessário.
- Propriedades comerciais idênticas com IDs diferentes permanecem entidades distintas; deduplicação/unicidade não é resolvida por equality.
- Venue e AssetClass dão contexto mínimo sem antecipar taxonomia completa, derivativos ou convenções de uma exchange.
- Increments suportam mercados inteiros e fracionários; validação não corrige silenciosamente preço/quantidade.
- Aritmética permanece limitada ao decimal .NET; regras de Order e persistência continuam fora deste modelo.

## Alternatives

Usar Symbol como identidade global criaria colisões entre venues. Usar somente Symbol + Venue como identidade interna acoplaria referências ao texto comercial e anteciparia uma regra de unicidade sem catálogo. Guid sem wrapper perderia a distinção de tipo e a validação de valor vazio.

Um record Instrument com igualdade automática por todas as propriedades confundiria metadata e identidade. Uma base Entity genérica, hierarquia de AssetClass ou catálogo global de venues adicionaria estrutura sem caso de uso nesta etapa. Divisão seguida de Round/Floor para alinhamento introduziria operações desnecessárias e risco de aceitar valores corrigidos.

## Deferred Decisions

Adiar catálogo, aliases/unicidade natural, metadata/lifecycle, persistência/mappings, temporalidade, derivativos, Market Data, Trading e integrações externas até requisitos e revisão arquitetural próprios.

## References

- ADR-002 — Financial Numeric Types.
- ADR-004 — Financial Domain Primitives.
- Domain Foundation e Market Instrument Foundation.
