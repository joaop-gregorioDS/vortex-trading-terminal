# Market Data Availability Semantics — TASK 0.17

## Occurrence and Availability

Occurrence Time identifica quando uma observação pertence historicamente ao mercado. ReplayEvent.ReplayTime continua sendo essa posição canônica. AvailabilityTime identifica o primeiro instante virtual no qual o payload canônico completo pode ser observado segundo esta política histórica.

São dimensões distintas, mesmo quando os valores coincidem. Não substituir ReplayTime, alterar modelos ou armazenar AvailabilityTime derivável no envelope/payload.

| Payload | ReplayTime / occurrence | AvailabilityTime |
|---|---|---|
| Quote | EventTime | EventTime |
| Trade | EventTime | EventTime |
| Candle completo | OpenTime | CloseTime |

## Application Policy

ReplayEventAvailability é uma classe estática sem campos em VortexTrading.Application/MarketData/Replay. GetAvailabilityTime(ReplayEvent? replayEvent) retorna UtcInstant e rejeita null com ArgumentNullException. O switch usa ReplayEventKind existente; um kind não suportado falha explicitamente, sem fallback silencioso.

A política usa diretamente Quote.EventTime, Trade.EventTime ou Candle.CloseTime. Não refaz a aritmética do intervalo do Candle nem depende de Session, ReplayTimeline, provider, storage, cultura, thread ou relógio real. Não há registro DI, cache ou estado de execução.

Chamadas repetidas retornam o mesmo valor de UtcInstant, independentemente da ordem de chamadas. Igualdade de instante é igualdade de valor, incluindo precisão de ticks. Candle.CloseTime já é uma propriedade derivada do Domain; não há promessa de identidade referencial entre resultados dessa propriedade.

## Quote, Trade and ReceivedTime

Quote e Trade são historicamente disponíveis em EventTime nesta primeira política. ReceivedTime é preservado como metadata, inclusive quando anterior, igual ou posterior a EventTime, e não determina disponibilidade.

Isso é uma convenção de historical replay e fundação determinística de backtesting. Não afirma que dados live chegam em EventTime. Latências de rede, exchange, provider e processamento, entrega fora de ordem, correções e precisão temporal da ingestão precisam de decisões separadas.

## Complete Candle Boundary

O intervalo do Candle é [OpenTime, CloseTime). O payload OHLCV completo é considerado disponível em CloseTime, inclusive, exatamente nesse instante, sem adicionar ou subtrair ticks.

Um candle de 1 minuto aberto às 10:00 mantém ReplayTime 10:00 e AvailabilityTime 10:01. Seus High, Low, Close e Volume finais não devem ser expostos a uma estratégia às 10:00. Esta política não transforma o Candle em eventos de abertura/fechamento ou atualizações parciais.

## Canonical Order and Causal Delivery

HistoricalReplayDataset permanece ordenado por ReplayTime e seus desempates existentes. ReplaySession também permanece inalterada, entregando ocorrências nessa ordem. A política calcula disponibilidade isoladamente e não reordena ou bloqueia entregas.

Exemplo:

| Evento | ReplayTime | AvailabilityTime |
|---|---|---|
| Candle A de 5 minutos | 10:00 | 10:05 |
| Trade B | 10:03 | 10:03 |

A ordem canônica de ocorrência é Candle → Trade. A cronologia de disponibilidade é Trade → Candle. Elas podem divergir sem destruir a identidade histórica. AvailabilityTime pode inclusive ultrapassar dataset.EndTime, que permanece o maior ReplayTime, não o fim da disponibilidade dos payloads.

Uma futura execução causal deverá respeitar AvailabilityTime ao liberar informações. Esta TASK não implementa essa execução nem define novos desempates causais, buffers ou horizonte temporal de conclusão. A ReplaySession atual não se torna segura para conectar estratégias apenas porque a política existe.

## Preservation and Determinism

O cálculo não clona envelope/payload, modifica valores financeiros, reescreve EventTime/ReceivedTime/OpenTime ou altera Dataset/Session/Timeline. Não há relógio de parede, pacing, background execution ou I/O. A assinatura exige somente um ReplayEvent válido, sem Session para calcular o resultado.

## Validation and Deferred Decisions

Application.Tests cobre Quote/Trade com ReceivedTime antes/igual/depois, candles de 1 minuto/5 minutos/1 hora/intervalo customizado, fechamento exato, null, preservação de referências e valores, repetibilidade com chamadas intercaladas, divergência entre ocorrência/disponibilidade e limites temporais suportados.

Adiar execução causal/availability-aware Session, integração com ReplaySession, Backtesting, Strategy Engine, indicators/order execution, semântica live/latência, parcialização de candles, CandleOpen/CandleClose, causal tie ordering, pacing/speeds, seek/rewind/reset, providers, DI, API/SignalR/frontend, persistence/Redis.

ADR-015 permanece Proposed até revisão arquitetural. Esta TASK não autoriza staging ou commit.
