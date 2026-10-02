# ADR-007 — Market Data Core Model

- Status: Accepted
- Date: 2026-10-01.
- Scope: Snapshots canônicos Quote e Candle, sem Trade, ingestão, streaming ou persistência.

## Context

Primitives financeiras, identidade de instrumento e semântica temporal estão aprovadas. O próximo passo é expressar observações bid/ask e OHLCV sem acoplar o Domain a providers ou criar um evento genérico cheio de campos opcionais. Quote observa dois lados em um instante; Candle descreve um intervalo agregado. Eles têm invariantes e temporalidade diferentes.

## Decision

Modelar Quote e Candle como sealed records imutáveis com construção validada, propriedades get-only e igualdade/hash estruturais. Não criar MarketEvent, MarketDataEvent, TimedEvent ou Entity base, QuoteId/CandleId ou herança entre snapshots.

Quote exige InstrumentId, EventTime, ReceivedTime, BidPrice, BidQuantity, AskPrice e AskQuantity. Ambos os preços são positivos e Bid <= Ask; locked é permitido, crossed é rejeitado. Quantidades não negativas incluem zero/frações, sem interpretar zero como remoção de lado. EventTime e ReceivedTime são UtcInstant preservados sem fallback ou pressuposição de ordem entre relógios. Quotes unilaterais ficam fora do contrato inicial.

Candle exige InstrumentId, Interval, OpenTime, Open, High, Low, Close e Volume. OHLC é positivo e Open/Close devem estar dentro de Low/High, com High >= Low. Flat é permitido; Volume usa Quantity não negativa, inclusive zero e frações. Candle não recebe ReceivedTime.

Derivar CloseTime exclusivamente de OpenTime + Interval, preservando ticks e limites [OpenTime, CloseTime). Validar na construção se a duração cabe nos ticks restantes até DateTimeOffset.MaxValue; quando não couber, lançar DomainException. O limite máximo exato é permitido. Não armazenar CloseTime independente nem realizar ajuste de precisão.

Snapshots carregam somente InstrumentId. A validação de tick/quantity increment ocorrerá fora destes modelos, em uma fronteira com metadata do instrumento. Não alterar Price ou Quantity globais para impor regras específicas de Quote/Candle. Não implementar CandleState, agregação ou inferência de finalização nesta fase.

## Consequences

- Contratos pequenos distinguem observação bid/ask de OHLCV, sem campos sem significado em um modelo específico.
- Quote preserva os tempos de ocorrência e ingestão; Candle preserva intervalo agregado sem presumir estado de fechamento.
- Modelo canônico inicial rejeita crossed quotes e preços zero, exigindo tratamento explícito de dados incompatíveis na futura normalização.
- Quantidades/volume zero e fracionários não pressupõem regras de exchange ou lote.
- CloseTime não pode divergir do intervalo; overflow temporal é rejeitado antes de existir um snapshot inválido.
- Igualdade estrutural distingue revisões de OHLCV e observações recebidas em instantes diferentes; ela não define deduplicação ou chave de persistência.
- Integridade estrutural não garante tick/step válido, existência do instrumento ou disponibilidade de negociação. Essas validações precisarão de contexto externo ao snapshot.
- Candle sem estado não prova que seus dados já estavam disponíveis/finalizados para uma estratégia histórica; a futura pipeline deverá preservar essa distinção para Backtesting.
- Domain permanece sem dependências externas; infraestrutura, persistência, frontend e contratos de Application/API não mudam.

## Alternatives

Um MarketEvent genérico com campos opcionais permitiria estados sem semântica clara e misturaria instantâneos com intervalos. Herança apenas para compartilhar InstrumentId anteciparia uma abstração sem comportamento comum suficiente. Identidade própria de snapshot ou igualdade só por InstrumentId/tempo confundiria entidade, observação e revisão.

Carregar Instrument completo acoplaria snapshots a versões de metadata e consulta de catálogo. Armazenar CloseTime independente permitiria inconsistência temporal. Derivar por soma sem verificação deixaria a falha para leitura da propriedade; saturar/truncar alteraria o intervalo silenciosamente. Alterar primitives globais para rejeitar zero afetaria regras de outros contextos já aprovadas.

## Deferred Decisions

Adiar Trade, identidade/sequences de provider, dados unilaterais/crossed fora do contrato, normalização e interfaces de provider, catálogo/increments, deduplicação, order book, agregação/finalização/correções de candle, CandleState, disponibilidade no Replay/Backtesting, relógios, persistência, transporte em tempo real e APIs.

## References

- ADR-004 — Financial Domain Primitives.
- ADR-005 — Market Instrument Identity.
- ADR-006 — Temporal Semantics.
- Market Data Core — TASK 0.9.
