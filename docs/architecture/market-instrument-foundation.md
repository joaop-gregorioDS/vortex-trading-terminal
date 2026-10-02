# Market Instrument Foundation — TASK 0.7

Fundação de Markets sobre o checkpoint `55d57ad`, sem persistência, catálogo, Application/API ou Market Data. Instrument identifica um instrumento negociável e associa as primitives aprovadas aos seus increments.

## Identidade

`InstrumentId` é a identidade interna estável. Usa Guid não vazio, aceita reconstrução explícita por construtor/`From(Guid)` e geração local por `New()`. A geração utiliza `Guid.NewGuid()`; não deriva de Symbol/Venue nem depende do banco. `ToString()` usa o formato Guid `D`, com hífens e hexadecimal minúsculo.

`Symbol + Venue` é uma identidade natural/comercial relevante, sem garantia global de unicidade nesta etapa. O mesmo Symbol pode existir em venues diferentes. Nenhum objeto conhece os demais Instruments ou impõe unicidade; resolução de catálogo, aliases e restrições relacionais serão decisões futuras.

Instrument é uma classe selada com `IEquatable<Instrument>`. Igualdade, `Equals(object)`, `==`, `!=` e hashing usam somente Id. Mesmo Id significa mesma identidade conceitual, inclusive quando as propriedades diferem; propriedades iguais com IDs distintos não tornam as entidades iguais. Isso não valida a consistência de versões de metadata: uma futura fronteira de catálogo deverá controlar essa questão. Hash não é identificador persistente.

## Componentes e invariantes

| Componente | Representação | Regra |
| --- | --- | --- |
| Id | `InstrumentId` | Obrigatório; Guid não vazio garantido pela primitive |
| Symbol | `Symbol` | Obrigatório; normalização previamente aprovada |
| Venue | `Venue` | Obrigatório; rejeita null/vazio/whitespace e aplica `Trim().ToUpperInvariant()` |
| AssetClass | Enum fechado | Other, Equity, ETF, Future, Option, Forex, Crypto, Index, FixedIncome |
| Currency | `Currency` | Obrigatória; unidade monetária associada ao instrumento |
| PriceIncrement | `Price` | Obrigatório e estritamente positivo |
| QuantityIncrement | `Quantity` | Obrigatório e estritamente positivo, inclusive fracionário |

Venue é um value object imutável com igualdade/hash pelo texto normalizado. Não há catálogo, regex ou regra específica de exchange. Venue não pretende resolver taxonomia de mercados/segmentos nem substituir identificadores oficiais.

AssetClass tem valores explícitos, com `Other = 0` como classificação genérica válida. Instrument rejeita casts numéricos indefinidos com DomainException. Não há flags, inferência automática de classe ou regras específicas por categoria. Uma classificação Future/Option não acrescenta metadata de derivativos.

Instrument exige os seis componentes de referência e valida classificação/increments na construção, usando DomainException. É imutável, com propriedades get-only, sem setters, lifecycle ou base Entity. Usa diretamente Currency, Price e Quantity; não introduz strings/decimals paralelos. Currency não atribui moeda automaticamente ao valor Price e não fornece FX.

## Alinhamento de preço e quantidade

`IsValidPrice(Price)` e `IsValidQuantity(Quantity)` verificam alinhamento ao incremento do instrumento:

```csharp
price.Value % PriceIncrement.Value == 0
quantity.Value % QuantityIncrement.Value == 0
```

O operador `%` trabalha com operands decimal e retorna o resto. Não calcula um quociente decimal intermediário para depois arredondar ou testar se é inteiro. Os increments positivos impedem divisor zero; entradas null geram DomainException. Os métodos só retornam alinhamento e não alteram valor, escala ou instrumento.

| Incremento | Valores alinhados | Valor desalinhado |
| --- | --- | --- |
| Price 0.05 | 0, 10.00, 10.05, 10.10 | 10.07 |
| Price 0.00000001 | 1.23456789 | 1.234567891 |
| Quantity 100 | 0, 100, 200 | 150 |
| Quantity 0.001 | 0, 0.001, 0.005 | 0.0015 |

Zero é múltiplo de todo incremento positivo. Estes métodos não validam uma Order, disponibilidade do instrumento, limites de negociação ou compatibilidade monetária de uma operação. Positividade de preços/quantidades de Order pertencerá àquele contexto.

## Política decimal e testes

Mantém ADR-002 e ADR-004: Price/Quantity globais ainda permitem zero; a regra de incremento positivo pertence a Instrument. Não há Round/Floor/Ceiling, conversão para float/double, tolerância, quantização ou escala fixa. A construção preserva os valores recebidos; precisão continua limitada à representação decimal do .NET.

Domain.Tests adiciona cobertura de Venue, InstrumentId, AssetClass e Instrument sem novos pacotes. Os testes verificam normalização em `tr-TR`/`pt-BR`, componentes obrigatórios, classificação indefinida, increments zero, identidade, valores desalinhados e preservação de escala. Incluem escala de 28 casas e `decimal.MaxValue` com incremento mínimo positivo para proteger contra algoritmos baseados em divisão que poderiam exceder o alcance do quociente. Somente o teste de `InstrumentId.New()` usa geração de Guid; não há avaliação estatística de aleatoriedade ou dependência de um valor gerado específico.

## Fronteiras e decisões adiadas

Domain continua BCL-only, sem pacotes, referências a projetos/frameworks externos ou atributos de serialização/persistência. DbContext, DbSets, migrations, API, frontend, infraestrutura e referências de projetos permanecem inalterados. PostgreSQL/Redis não são usados pelo modelo ou pelos testes.

Adiam-se catálogo/unicidade comercial, aliases, atualização e consistência de metadata, representação relacional, repositories, entidades de Trading, Quote/Candle e política temporal de Market Data. Também ficam adiados timezone, sessões, timestamps, vencimentos, strike, underlying, multiplicador, liquidação e dados de display. Nenhuma escolha de Venue/AssetClass antecipa essas regras.

Referências: ADR-005 — Market Instrument Identity; [operadores aritméticos C# e resto decimal](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/arithmetic-operators#remainder-operator-).
