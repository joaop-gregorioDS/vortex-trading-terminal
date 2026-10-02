# ADR-008 — Trade Identity and Semantics

- Status: Accepted
- Date: 2026-10-02.
- Scope: Observação canônica Trade, sem identidade artificial, metadata de provider ou ingestão.

## Context

As fundações financeiras, de instrumento e temporais estão aprovadas, e Quote/Candle já expressam observações bid/ask e OHLCV. Falta representar o negócio efetivamente executado com um contrato pequeno e independente de provider.

Ainda não existem contratos concretos de fontes para estabelecer exchange/provider trade ID, sequence, aggressor side ou condições do negócio. Antecipar um identificador universal ou deduplicação por conteúdo confundiria igualdade de valores com identidade de eventos do feed.

## Decision

Trade is an immutable market observation.

No artificial canonical TradeId is introduced yet.

Structural equality is not feed deduplication.

Provider-specific identity, sequence and trade metadata remain ingestion concerns until concrete provider contracts exist.

Implementar Trade como sealed record com propriedades get-only: InstrumentId, EventTime, ReceivedTime, Price e Quantity. Todos são obrigatórios; componentes null são rejeitados com DomainException.

Exigir Price > 0 e Quantity > 0 neste modelo, preservando as primitives globais. Frações e extremos positivos representáveis são válidos, sem arredondamento, quantização ou alteração de escala. Não validar price tick, quantity increment ou lot size sem metadata externa do instrumento.

Preservar EventTime e ReceivedTime como UtcInstant independentes, sem pressupor ordem entre relógios, fallback ou timestamps gerados. Usar igualdade/hash estruturais dos cinco componentes, sem deduplicação, TradeId, ID gerado ou base MarketEvent/Entity.

## Consequences

- Quote, Trade e Candle expressam conceitos distintos com modelos explícitos e imutáveis.
- Zero continua válido em Price/Quantity globais, em quantidades de Quote e volume de Candle; Trade exige negócio com preço e quantidade positivos.
- Trade não carrega Instrument, Symbol ou Venue duplicados; validação de catálogo/increments exige contexto externo.
- Valores iguais não provam que duas entregas representam o mesmo negócio; ReceivedTime diferente também não prova negócios distintos.
- A pipeline futura precisará preservar identidade e metadata da fonte antes de decidir deduplicação, ordenação ou correções.
- O modelo inicial não representa aggressor side ou trade conditions; dados incompatíveis com suas invariantes exigirão decisão explícita na futura normalização.
- Domain permanece puro, sem novos pacotes, relógio, persistência, transporte ou contratos de provider.

## Alternatives

Gerar um Guid/TradeId artificial daria identidade diferente a cada construção, sem ajudar a reconhecer reentregas da fonte. Impor um ExchangeTradeId/ProviderTradeId universal anteciparia contratos que variam entre providers. Usar uma chave composta de InstrumentId, EventTime, Price e Quantity como identidade/deduplicação poderia colapsar negócios distintos com conteúdo igual.

Uma Entity/MarketEvent base compartilharia poucos campos e anteciparia semânticas de identidade ou ingestão. Carregar Instrument completo acoplaria observações à metadata. Alterar Price/Quantity globais para rejeitar zero quebraria regras aprovadas em outros modelos. Adicionar sequence, lado ou condições opcionais antes do provider misturaria informação observada, inferida e dependente da fonte.

## Deferred Decisions

Adiar provider/exchange/external trade IDs e seu envelope de ingestão; sequence e seu escopo; aggressor side fornecido versus inferido; trade condition codes, auction/cross/block/odd lot/off-book; correções e cancelamentos; deduplicação; catálogo/increments; interfaces de provider e normalização; agregação de candles; Replay/Backtesting; persistência e transporte.

## References

- ADR-004 — Financial Domain Primitives.
- ADR-005 — Market Instrument Identity.
- ADR-006 — Temporal Semantics.
- ADR-007 — Market Data Core Model.
- Trade Foundation — TASK 0.10.
