# Replay Session Foundation — TASK 0.16

## Placement and Ownership

ReplaySession pertence a VortexTrading.Application/MarketData/Replay. Coordena HistoricalReplayDataset, ReplayTimeline e ReplayEvent, sem armazenamento, adapters ou dependência de IMarketDataProvider. Cada instância representa uma execução independente sobre um dataset explícito, não nulo e imutável.

A Session possui uma única ReplayTimeline privada criada no construtor e seu próprio Position. Não expõe dataset ou timeline mutável, não usa estado estático/global e não registra DI. CurrentTime delega diretamente à timeline; não há segunda cópia temporal.

## Initial State and Progress

Inicialmente, CurrentTime = dataset.StartTime, Position = 0 e IsCompleted = false. Estar em StartTime não significa que um evento foi consumido. Position conta ocorrências já entregues, não timestamps distintos, e só pode ser alterado internamente.

IsCompleted é derivado de Position == dataset.Count. O dataset existente garante pelo menos um evento. Após a última entrega, Position = Count, IsCompleted = true e CurrentTime = dataset.EndTime.

## Synchronous Pull Operation

A API mínima é bool TryMoveNext(out ReplayEvent? replayEvent), com NotNullWhen(true) para comunicar a garantia de nullability ao consumidor. Não há IAsyncEnumerable, CancellationToken, I/O ou operação bloqueante. Ler propriedades não avança a execução.

Para cada chamada com evento disponível:

1. Obter dataset.Events[Position].
2. Executar ReplayTimeline.AdvanceTo(event.ReplayTime).
3. Incrementar Position.
4. Retornar true e a mesma instância de ReplayEvent.

Quando a chamada retorna true, CurrentTime == replayEvent.ReplayTime e Position já inclui aquela ocorrência. A timeline nunca é antecipada para o próximo evento. O parâmetro out começa como null; se AdvanceTo falhar, Position não avança e nenhuma ocorrência é reportada como consumida. O dataset canônico e a timeline privada tornam essa falha inválida no fluxo normal.

Essa é atomicidade lógica de uma operação síncrona com consumo serial, não uma garantia de acesso concorrente à mesma Session. Cada execução tem um consumidor responsável pela ordem das chamadas; não há locks ou controle concorrente nesta fundação.

## Canonical Order, Same Time and Duplicates

A Session percorre os índices existentes do dataset sem ordenar novamente, filtrar instrumentos/kinds ou deduplicar. Quote, Trade e Candle são entregues no mesmo fluxo heterogêneo como ReplayEvent. A ordem já estabelecida por ReplayTime, InstrumentId, Kind e ordinal permanece intacta.

Eventos com ReplayTime igual avançam Position individualmente enquanto CurrentTime permanece igual. A mesma instância de ReplayEvent repetida, envelopes distintos sobre o mesmo payload e payloads estruturalmente iguais representam ocorrências separadas e são todos entregues.

## Payload Preservation and Completion

Os envelopes e payloads Quote/Trade/Candle são as mesmas instâncias do dataset, sem clone, reconstrução, normalização financeira ou reescrita de ReceivedTime, EventTime e OpenTime. Tempo virtual é estado da Session.

Depois da conclusão, TryMoveNext retorna false e evento null em todas as chamadas posteriores. Position, CurrentTime e IsCompleted permanecem estáveis; terminar não lança exceção. Dataset.Events, Count, StartTime e EndTime não são modificados.

## Independence and Restart

Duas Sessions sobre o mesmo dataset compartilham somente os dados imutáveis. Timeline e progresso são independentes. Avançar ou concluir uma não altera a outra. Para reiniciar, criar new ReplaySession(dataset). Não há Reset, Restart, Rewind, Seek ou setter público de Position.

## Wall Clock and Pacing

O consumidor solicita uma ocorrência e a Session a entrega imediatamente após atualizar tempo virtual/progresso. Não há Now/UtcNow, TimeProvider.System, Stopwatch, Task.Delay, Thread.Sleep, timers, playback speed ou background loop. A passagem de tempo histórico decorre somente do ReplayTime da ocorrência entregue.

## Look-Ahead Boundary

Coerência entre CurrentTime e ReplayTime não resolve disponibilidade de Market Data. Candle continua contendo OHLCV final e seu ReplayTime permanece OpenTime. Entregá-lo nessa posição histórica não autoriza uma estratégia a conhecer seus valores finais naquele instante.

Não há Strategy Engine, Backtesting ou transformação em CandleOpen/CandleClose/partial candle. A política de disponibilidade e prevenção de look-ahead deverá ser formalizada separadamente antes de conectar estratégias.

## Validation and Deferred Decisions

Application.Tests cobre construção, primeira entrega, dataset heterogêneo completo, empates temporais, ocorrências duplicadas, igualdade estrutural, conclusão estável, independência/reinício, imutabilidade do dataset, ordem canônica não trivial, limites temporais e ticks, payloads e ausência de antecipação temporal.

Adiar disponibilidade/look-ahead, Backtesting, Strategy Engine, controllers, async/cancelamento, pacing/speeds, seek/rewind/reset, execução em background, integração com providers, DI, API/SignalR/frontend, persistence/Redis, identidade, sequence e deduplicação.

ADR-014 permanece Proposed até revisão arquitetural. Esta TASK não autoriza staging ou commit.
