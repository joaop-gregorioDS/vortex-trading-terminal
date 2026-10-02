# Trade Foundation

## Significado e representação

Trade representa um negócio executado/observado no mercado para determinado instrumento. O vocabulário canônico distingue três observações:

| Modelo | Significado | Temporalidade |
| --- | --- | --- |
| Quote | Estado do melhor bid/ask, intenção/liquidez disponível | EventTime e ReceivedTime |
| Trade | Negócio efetivamente executado/observado | EventTime e ReceivedTime |
| Candle | Representação OHLCV de um intervalo | OpenTime e CloseTime derivado |

Trade é um sealed record imutável, com construção validada e propriedades get-only. Não possui identidade artificial, herança de Entity/MarketEvent ou comportamento de ingestão.

## Componentes obrigatórios

| Componente | Tipo | Semântica |
| --- | --- | --- |
| InstrumentId | InstrumentId | Referência ao instrumento observado |
| EventTime | UtcInstant | Quando o negócio ocorreu segundo a fonte/mercado |
| ReceivedTime | UtcInstant | Quando o Vortex recebeu/aceitou o dado |
| Price | Price | Preço executado, estritamente positivo |
| Quantity | Quantity | Quantidade executada, estritamente positiva |

Qualquer componente null é rejeitado com DomainException. InstrumentId não duplica Instrument, Symbol ou Venue. A existência do instrumento e sua metadata dependem de catálogo/contexto externo.

## Preço, quantidade e precisão

Trade exige Price > 0 e Quantity > 0. As primitives globais Price e Quantity continuam aceitando zero; a regra mais restrita pertence a este modelo de negócio executado. Quote permite quantidades zero, e Candle permite volume zero.

Quantidades fracionárias, como 0.001, são válidas. Não há imposição de quantidade inteira, lote, quantidade mínima ou increment. Também não há validação de price tick: Trade contém somente InstrumentId e não dispõe de metadata suficiente para essa decisão. Uma futura fronteira com acesso ao catálogo deverá validar compatibilidade com o instrumento.

Os valores decimal são preservados integralmente, inclusive escala, alta precisão, menor valor positivo e decimal.MaxValue. Não há cálculo de notional, conversão para floating point, epsilon/tolerância, arredondamento ou quantização.

## Semântica temporal

EventTime e ReceivedTime são componentes distintos, preservados como UtcInstant com precisão de ticks e normalização UTC já definida pela primitive.

Não existe exigência de ReceivedTime >= EventTime. Recebimento anterior, igual ou posterior é aceito, sem fallback ou substituição silenciosa de um timestamp pelo outro. Trade não consulta relógio nem gera timestamps; o chamador fornece ambos explicitamente.

## Igualdade e deduplicação

A igualdade estrutural e o hash consideram InstrumentId, EventTime, ReceivedTime, Price e Quantity. Dois valores com todos esses componentes equivalentes são iguais; diferenças em qualquer componente produzem observações estruturalmente diferentes.

Igualdade estrutural não equivale a deduplicação de feed. Duas observações iguais podem corresponder a negócios distintos com os mesmos dados, ou a entregas repetidas do mesmo negócio. Da mesma forma, uma reentrega com ReceivedTime diferente pode ser estruturalmente diferente. O modelo isolado não resolve essas situações.

Não existe TradeId canônico artificial ou geração de Guid. Identidade da fonte e regras de deduplicação exigirão contratos concretos de provider. A futura pipeline não deverá usar igualdade estrutural como critério universal para descartar negócios.

## Metadata adiada e decisões futuras

- ProviderTradeId, ExchangeTradeId e ExternalTradeId: adiados; metadata/envelope de ingestão poderá preservar identificadores específicos da fonte.
- Sequence/SequenceNumber: adiados até existir contrato de normalização/ingestão que defina escopo, ordenação e significado da sequência.
- Aggressor side: adiado; informação fornecida pela exchange e inferência por quote/tick exigem semânticas explícitas distintas.
- Trade conditions: auction, cross, block trade, odd lot, correction, cancel, off-book e códigos de condição dependem de venue/provider e ficam adiados.
- Catálogo e increments, deduplicação, correções/cancelamentos, provider interfaces, normalização, agregação, Replay/Backtesting, persistência e transporte serão tratados em tarefas próprias.

Esta fundação não implementa pipeline, provider, relógio, agregação ou integração. Application, API, Infrastructure, frontend e persistência permanecem inalterados.

## Validação e referência

TradeTests cobre construção, nulls, zero rejeitado, valores positivos/fracionários, precisão/escala e extremos decimal, preservação/ordenação independente dos tempos, extremos temporais, igualdade/hash, diferença por cada componente, independência de cultura e ausência de validação interna de increments. Os testes usam instantes e identificadores fixos, sem relógio real ou aleatoriedade.

A decisão arquitetural está registrada no ADR-008 — Trade Identity and Semantics, com status Proposed para revisão.
