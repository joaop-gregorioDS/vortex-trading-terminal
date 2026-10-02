# Market Data Orchestration — TASK 0.13

## Responsibility and Placement

MarketDataOrchestrator pertence a VortexTrading.Application/MarketData/Orchestration. Um consumer entrega uma assinatura ao orchestrator, que valida a requisição e a capacidade do único IMarketDataProvider recebido no construtor, retornando o stream canônico correspondente.

Consumer → MarketDataOrchestrator → IMarketDataProvider → adapter → Quote / Trade / Candle.

O único objeto de serviço recebido é IMarketDataProvider; os tipos de entrada e saída vêm do Domain e os metadados vêm da porta existente de Application. O componente não conhece Infrastructure ou SimulationMarketDataProvider e não possui interface adicional, registro em DI ou seleção de providers.

Provider null é rejeitado no construtor. ProviderDescriptor delega diretamente a Descriptor da instância recebida, preservando identidade e metadados sem cópia ou novo descriptor.

## Subscription Validation and Snapshot

As três operações validam a assinatura na chamada, antes de invocar o provider:

- Coleção null: ArgumentNullException para instrumentIds.
- Coleção vazia ou com elemento null: ArgumentException para instrumentIds.
- CandleInterval null: ArgumentNullException para interval, após validar a coleção.

Vazio não significa subscribe-all. IDs válidos são copiados para um snapshot próprio, deduplicados por igualdade de InstrumentId e ordenados pelo Guid em InstrumentId.Value, usando o comparador padrão de Guid. A normalização não altera a coleção original.

[B, A, A] e [A, B] produzem a mesma assinatura ordenada. Isso normaliza a assinatura; não deduplica os eventos do feed.

O provider recebe uma coleção somente de leitura sobre o array privado normalizado. Alterações posteriores na coleção do chamador, antes ou durante a enumeração, não alteram a assinatura entregue. Cada chamada cria seu próprio snapshot. Modificação concorrente durante a própria cópia não é suportada; o chamador deve estabilizar a coleção durante a chamada.

## Capabilities and Typed Operations

StreamQuotesAsync exige Quotes; StreamTradesAsync exige Trades; StreamCandlesAsync exige Candles. A condição é (advertised & required) == required, permitindo capabilities combinadas. Capability ausente causa NotSupportedException síncrona, sem invocar a operação do provider.

As operações retornam, respectivamente, IAsyncEnumerable<Quote>, IAsyncEnumerable<Trade> e IAsyncEnumerable<Candle>. CandleInterval é entregue como a mesma instância solicitada, sem agregação ou alteração de duração. As assinaturas de IMarketDataProvider e seus tipos de metadata permanecem inalteradas.

## Transparent and Lazy Forwarding

Após as guardas, o orchestrator chama a operação tipada do provider e retorna exatamente o IAsyncEnumerable recebido. A chamada do método do provider ocorre nesse momento; a enumeração dos eventos continua lazy e dirigida pelo consumer. O orchestrator não chama GetAsyncEnumerator ou MoveNextAsync e não materializa dados em listas, arrays ou buffers.

Os eventos atravessam a camada preservando suas instâncias, ordem e quantidade. Não há clonagem, filtros, reordenação, validação adicional dos eventos ou alteração de timestamps/valores financeiros. Dados repetidos ou fora de ordem continuam repetidos ou fora de ordem conforme fornecidos pelo provider. A canonicalidade dos eventos é responsabilidade do Domain e do adapter.

## Cancellation, Failures and Lifecycle

O CancellationToken da operação chega ao provider sem alteração. Como o stream é retornado diretamente, o token de GetAsyncEnumerator/WithCancellation chega à enumeração do próprio provider, que aplica as regras padrão .NET da porta. Não há token substituto, timeout, tarefa auxiliar ou captura de OperationCanceledException.

Exceções síncronas da operação e falhas durante a enumeração propagam como os mesmos objetos, sem wrapping, tradução, retry ou fallback. O consumer continua responsável por enumerar e descartar o enumerador; o orchestrator não intercepta esse ciclo de vida.

Não há estado de progresso global, sessão em background ou buffering. Cada requisição possui seu snapshot. A independência e a repetição dos eventos dependem das garantias do provider; o orchestrator não acrescenta garantias universais de replay ou concorrência à porta.

## Test Boundary

Application.Tests continua referenciando apenas Application, com Domain transitivo. RecordingMarketDataProvider é um test double local mínimo que registra chamadas, assinatura, intervalo e token e entrega sequências previamente definidas. Nenhum teste de Application depende de Infrastructure ou do Simulation provider.

Os testes cobrem guardas síncronas, capability ausente e combinada, deduplicação/ordenação, snapshot protegido contra alteração do chamador, forwarding da mesma instância do stream e dos eventos, consumo lazy, descarte antecipado, tokens da operação/enumeração, falhas originais e requisições independentes. Sequências propositalmente repetidas e fora de ordem demonstram a ausência de filtragem ou reordenação.

## Limits and Deferred Decisions

Esta fundação coordena exatamente um provider recebido. Não implementa registry, fallback, failover, load balancing, cache, event bus, broker, BackgroundService ou worker. PostgreSQL, Redis, API, frontend e composição de DI permanecem fora do consumo.

Adiar Historical Replay, providers reais, clock de replay, raw envelopes, normalização de eventos, sequences, deduplicação de feed, gap detection, candle aggregation, persistência, transporte, SignalR, retry/reconnect e políticas de lifetime/concorrência dos providers.

O ADR-011 permanece Proposed até revisão do arquiteto. Esta tarefa não autoriza staging ou commit.
