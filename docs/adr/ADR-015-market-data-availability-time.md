# ADR-015 — Market Data Availability Time

- Status: Accepted
- Date: 2026-10-02.
- Scope: Disponibilidade histórica de payloads completos, derivada em Application.

## Context

ReplayEvent, HistoricalReplayDataset e ReplaySession preservam a ocorrência histórica canônica. Candle é posicionado por OpenTime, mas seu payload contém OHLCV final do intervalo. Entregar esse payload a uma estratégia em OpenTime permitiria decisões com informação futura. Precisamos distinguir posicionamento histórico e disponibilidade antes de implementar execução causal ou Backtesting.

## Decision

Historical replay distinguishes canonical occurrence time
from causal availability time.

Quote and Trade canonical payloads become historically
available at EventTime.

A complete Candle payload becomes historically available
at CloseTime while its canonical ReplayTime remains
OpenTime.

AvailabilityTime is derived in Application and is not
stored in Domain payloads or ReplayEvent.

## Rationale and Policy

Introduzir somente ReplayEventAvailability em Application/MarketData/Replay, como componente estático sem estado, com GetAvailabilityTime(ReplayEvent? replayEvent). Rejeitar null com ArgumentNullException. Discriminar por ReplayEventKind existente, sem reflection, dynamic, strings ou object público; kind não suportado falha explicitamente.

Retornar Quote.EventTime, Trade.EventTime ou Candle.CloseTime diretamente. O Domain já deriva e valida CloseTime; a política não recalcula OpenTime + Interval. O resultado é determinístico por valor de UtcInstant, não por identidade de objeto para propriedades derivadas.

## Quote / Trade and ReceivedTime

EventTime determina disponibilidade histórica de Quote/Trade. ReceivedTime permanece metadata intacta, seja anterior, igual ou posterior a EventTime. Não simular latência ou interpretar ReceivedTime como instante de disponibilidade nesta política.

## Candle Semantics

O intervalo [OpenTime, CloseTime) termina no instante em que consideramos seu OHLCV completo disponível, CloseTime inclusive. ReplayTime permanece OpenTime. Um candle de 10:00 a 10:01 não disponibiliza High/Low/Close/Volume finais às 10:00. Não transformar payload, reconstruir envelopes ou criar eventos parciais/abertura/fechamento.

## Ordering Consequences

Dataset continua baseado em ReplayTime. Candle aberto às 10:00 e fechado às 10:05 precede historicamente um Trade às 10:03, enquanto a cronologia de disponibilidade é Trade 10:03 antes de Candle 10:05. Não implementar reorder nesta TASK.

AvailabilityTime pode ultrapassar dataset.EndTime; esse limite descreve ocorrência. Uma futura execução causal deverá respeitar disponibilidade sem destruir ordem/identidade histórica, definindo separadamente desempates e seu horizonte de conclusão.

## Historical / Live Distinction

Esta convenção serve a historical replay e fundação determinística de backtesting. Não promete chegada live em EventTime. Latências de rede, exchange, provider, processamento, entrega fora de ordem e correções permanecem problemas separados.

## Alternatives Considered

Substituir ReplayTime por CloseTime alteraria o posicionamento histórico de candles e a ordenação aprovada. Usar OpenTime como disponibilidade permitiria observar OHLCV final antecipadamente. Usar ReceivedTime nesta etapa misturaria execução histórica com políticas de ingestão/live ainda indefinidas.

Armazenar AvailabilityTime em Domain ou ReplayEvent criaria estado derivável capaz de divergir. Usar timeline/Session/relógio como entrada faria o resultado depender da execução. Integrar imediatamente uma Session causal ou dividir candles anteciparia decisões fora do escopo.

## Consequences and Limitations

- Ocorrência e disponibilidade tornam-se conceitos explícitos, preservando modelos e contratos existentes.
- A política é stateless e sem efeitos colaterais; não requer Session, storage, provider, DI ou relógio.
- Quote/Trade mantêm disponibilidade em EventTime; Candle completo usa CloseTime exato.
- Payloads, envelopes, metadata e valores financeiros permanecem intactos.
- Dataset e ReplaySession permanecem inalterados e não passam a impor disponibilidade por si mesmos.
- A política isolada não resolve toda prevenção de look-ahead: liberação causal precisa de execução posterior antes de conectar estratégias.

## Deferred Decisions

Execução causal, disponibilidade integrada à Session, Backtesting/Strategy Engine, indicators/order execution, causal tie ordering/horizonte, semântica live/latência/correções, candles parciais e CandleOpen/CandleClose, pacing/playback speeds, seek/rewind/reset, integração com providers, DI, API/SignalR/frontend, persistence/Redis.

## References

- ADR-012 — Historical Replay Time Semantics.
- ADR-014 — Replay Session Execution Model.
- Market Data Availability Semantics — TASK 0.17.
