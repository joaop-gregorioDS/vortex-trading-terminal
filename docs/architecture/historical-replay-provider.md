# Historical Replay Provider — TASK 0.15

## Placement and Dependency

HistoricalReplayProvider pertence a VortexTrading.Infrastructure/MarketData/Replay e implementa o IMarketDataProvider existente. Recebe explicitamente um HistoricalReplayDataset imutável, definido em Application. Dataset null causa ArgumentNullException no construtor.

Não há carga de arquivos ou banco, importer, pacote adicional ou registro em DI. Application, Domain, Replay Foundation, MarketDataOrchestrator e Simulation provider permanecem inalterados.

O descriptor é estável para a instância: Key = historical-replay, DisplayName = Historical Replay e Capabilities = Quotes | Trades | Candles, usando MarketDataProviderDescriptor existente.

## Stream Projection

Cada operação percorre HistoricalReplayDataset.Events na ordem já estabelecida. Não reordena, reconstrói ReplayEvents, altera ReplayTime ou deduplica ocorrências.

| Operation | Selection | Original output |
|---|---|---|
| StreamQuotesAsync | Quote e instrumento solicitado | Quote |
| StreamTradesAsync | Trade e instrumento solicitado | Trade |
| StreamCandlesAsync | Candle, instrumento e intervalo solicitado | Candle |

O intervalo é comparado por igualdade de valor de CandleInterval. Um novo objeto com a mesma Duration corresponde ao intervalo armazenado. Intervalo null causa ArgumentNullException na chamada. Intervalo válido sem correspondências produz stream vazio com conclusão normal, sem agregação.

Instrumentos válidos ausentes do dataset e tipos sem eventos correspondentes também produzem streams vazios. As capabilities descrevem operações suportadas, sem exigir que todo dataset contenha observações dos três tipos.

## Subscription Snapshot and Membership

As operações validam a assinatura na chamada: coleção null causa ArgumentNullException para instrumentIds; vazia ou com elemento null causa ArgumentException para instrumentIds. Vazio não significa subscribe-all.

O provider materializa somente os IDs em snapshot próprio e cria HashSet<InstrumentId> para membership, eliminando IDs repetidos por igualdade de valor. [A, A, B] equivale a [A, B]. A coleção original não é modificada; alterações posteriores, antes ou durante consumo, não afetam a seleção. A coleção deve permanecer estável durante a chamada que a copia.

O HashSet não é enumerado para produzir eventos: sua função é apenas Contains. A assinatura [A, B] não agrupa resultados por A e depois B. Se os eventos correspondentes aparecem como B, A, B, A no dataset, a saída continua B, A, B, A, qualquer que seja a ordem da assinatura.

## Relative Order and Occurrences

A garantia de cada operação é a ordem relativa dos eventos correspondentes em HistoricalReplayDataset. Repetições do mesmo payload e ocorrências estruturalmente iguais permanecem; apenas a assinatura é normalizada.

Quote, Trade e Candle são emitidos como as mesmas instâncias canônicas do dataset. Preços, quantidades, intervalos e timestamps permanecem intactos. ReceivedTime de Quote/Trade não é substituído por EventTime, tempo virtual ou relógio real.

Os três streams são independentes. Consumi-los separadamente não preserva automaticamente uma ordem global entre Quote, Trade e Candle. A ordem heterogênea continua representada no dataset; esta porta não realiza merge entre operações.

## Lazy, Finite Consumption

A operação pública valida e copia a assinatura e devolve um iterator assíncrono privado. Esse iterator só percorre o dataset em MoveNextAsync, selecionando e emitindo um payload por vez. Não existe lista, array, buffer, Channel ou índice de eventos filtrados. A materialização dos IDs não materializa os eventos.

O dataset já está em memória. await Task.CompletedTask permite usar o iterator assíncrono sem introduzir agendamento, pacing, background producer ou espera artificial. O fim do dataset completa normalmente o stream; não há loop infinito.

O helper genérico privado compartilha somente o percurso/projeção. Os métodos públicos continuam retornando os três tipos canônicos da porta, sem envelope público misto.

## Cancellation and Enumeration Independence

EnumeratorCancellation preserva tokens da operação e de GetAsyncEnumerator/WithCancellation conforme a semântica padrão .NET. Cancelamento é verificado ao iniciar, antes de cada evento do dataset, inclusive descartado por filtros, e antes da conclusão. Cancelar um stream sem correspondências ou após o último match também é observado. OperationCanceledException propaga sem tradução.

Cada enumeração possui seu enumerador local sobre o dataset, sem cursor ou contador global. Chamadas repetidas e reenumeração começam desde o início. O snapshot de membership não é alterado após construção; enumerações intercaladas, inclusive do mesmo stream, têm progresso e cancelamento independentes. O provider só lê o dataset imutável.

## Timeline, Wall Clock and Candle Availability

O provider não recebe, cria, possui ou avança ReplayTimeline. Colocar uma timeline compartilhada nestes três streams independentes produziria semântica incorreta para consumo heterogêneo. Uma futura sessão/controlador deverá coordenar o fluxo global e o avanço virtual.

Não há Now/UtcNow, TimeProvider.System, Stopwatch, timers, Thread.Sleep ou Task.Delay. Os eventos ficam disponíveis conforme o consumer solicita, sem simular segundos históricos, playback speed ou pacing.

Candle permanece posicionado por OpenTime na fundação existente. Projetar o candle não autoriza uma estratégia a conhecer antecipadamente OHLC final. Disponibilidade de candles fechados e prevenção de look-ahead continuam responsabilidades adiadas de Replay/Backtesting.

## Validation and Deferred Decisions

Infrastructure.Tests usa datasets pequenos, mistos e fora de ordem de entrada, com expectativas explícitas. Testa tipos/instrumentos/intervalos, ordem relativa, ocorrências repetidas, instâncias originais, snapshot da assinatura, completion vazio, cancelamento, reenumeração, independência entre enumerações e preservação decimal extrema. A mesma classe MarketDataOrchestrator consome Simulation e Replay através da porta sem alteração.

Um teste comprova que requisição e criação do enumerador são diferidas em relação ao consumo mesmo com token cancelado e zero matches. A revisão do iterator confirma percurso lazy e ausência de materialização dos eventos, sem testes de allocation/tempo ou instrumentação do dataset imutável.

Adiar Replay Session/controlador, merge heterogêneo e timeline global, disponibilidade/look-ahead, importer e formatos, banco/filesystem, persistência, DI, API, SignalR, frontend, registry/fallback, seek/rewind/reset, pacing/speeds, identidade/sequence e deduplicação de feed.

ADR-013 permanece Proposed até revisão arquitetural. Esta tarefa não autoriza staging ou commit.
