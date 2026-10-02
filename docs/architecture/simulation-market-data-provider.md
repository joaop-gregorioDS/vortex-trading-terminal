# Deterministic Simulation Market Data Provider

## Finalidade e localização

SimulationMarketDataProvider é o primeiro adapter concreto de IMarketDataProvider, em Infrastructure/MarketData/Simulation. Valida a porta de Application com observações Quote, Trade e Candle canônicas, offline, finitas e reproduzíveis. Não pretende reproduzir comportamento realista de mercado.

Descriptor estável: Key = simulation, DisplayName = Simulation, Capabilities = Quotes | Trades | Candles. Todas as operações anunciadas são suportadas; não há configuração, credencial, health ou estado de conexão no descriptor.

Domain e os contratos Application permanecem inalterados. Não há registro em DependencyInjection.cs ou Program.cs; a instância é construída explicitamente com configuração validada.

## Configuração imutável

SimulationMarketDataOptions é um sealed record com propriedades get-only:

| Campo | Tipo | Regra |
| --- | --- | --- |
| StartTime | UtcInstant | Obrigatório; base temporal explícita |
| Price | Price | Obrigatório e > 0 |
| Quantity | Quantity | Obrigatória e > 0; frações permitidas |
| EventCount | int | > 0; observações por instrumento distinto em cada stream |

Componentes null geram ArgumentNullException. Valores zero de preço/quantidade e contagem inválida geram ArgumentOutOfRangeException. As primitives globais não são alteradas.

Não há PriceStep, quantidade variável ou latency configurável nesta versão: valores financeiros constantes e latência lógica zero atendem à validação da porta com quatro parâmetros. A progressão temporal distingue os eventos sem introduzir aritmética financeira ou quantização.

## Geração e timestamps

Para rodada i, começando em zero, emitir uma observação para cada ID distinto em ordem crescente de Guid. EventCount rodadas produzem EventCount × quantidade de IDs distintos observações por assinatura.

| Stream | Tempo | Dados financeiros |
| --- | --- | --- |
| Quote | EventTime = StartTime + i segundos; ReceivedTime = EventTime | Bid = Ask = Price; quantidades dos dois lados = Quantity |
| Trade | EventTime = StartTime + i segundos; ReceivedTime = EventTime | Price e Quantity configurados |
| Candle | OpenTime = StartTime + i × Interval | Open = High = Low = Close = Price; Volume = Quantity |

Quotes são locked, válidos e nunca crossed. Trades têm preço e quantidade positivos. Candles são flat e gerados diretamente, sem agregação a partir de Trades; CloseTime continua derivado pelo Domain como OpenTime + Interval, com limite [OpenTime, CloseTime).

O CandleInterval solicitado é preservado, inclusive intervalos customizados e precisão de ticks. Instantes não são alinhados silenciosamente a fronteiras de calendário: o primeiro candle abre exatamente em StartTime.

ReceivedTime igual a EventTime é uma escolha desta simulação com latência lógica zero. Não representa premissa sobre redes reais nem altera a política geral do Domain. Nenhum timestamp depende do relógio do sistema, Random ou ID gerado.

## Decimal e limites temporais

Os mesmos valores imutáveis Price/Quantity são usados diretamente, preservando bits, escala e precisão decimal, inclusive o menor valor positivo e decimal.MaxValue. Não há soma, multiplicação, notional, arredondamento ou conversão financeira; portanto não há caminho de overflow decimal na geração.

Options verifica se StartTime + (EventCount - 1) segundos cabe até DateTimeOffset.MaxValue. Isso garante toda a sequência de Quote/Trade antes de existir um provider configurado com instantes inválidos. Uma única observação exatamente no máximo é permitida.

Candles validam o fechamento da série inteira na chamada: Interval.Ticks deve ser <= ticks restantes até MaxValue / EventCount. Essa comparação evita multiplicação potencialmente excedente. Uma série cujo último CloseTime é exatamente MaxValue é válida; séries que ultrapassam o limite geram ArgumentOutOfRangeException para interval antes da emissão.

O cálculo de cada offset usa ticks inteiros com multiplicação checked. As verificações anteriores garantem offsets e somas representáveis, sem wraparound, saturação ou truncamento. A configuração precisa permitir Quote/Trade mesmo quando o chamador pretende consumir apenas candles; não há configuração separada por shape nesta fundação.

## Assinaturas e instrumentos repetidos

Cada método valida a assinatura sincronamente antes de devolver o stream:

- Coleção null: ArgumentNullException para instrumentIds.
- Coleção vazia ou elemento null: ArgumentException para instrumentIds; vazio não significa todos os instrumentos.
- CandleInterval null: ArgumentNullException para interval.
- Séries de candles fora do limite temporal: ArgumentOutOfRangeException para interval.

O provider captura uma cópia da coleção, remove IDs repetidos por igualdade de InstrumentId e ordena explicitamente pelo valor Guid, usando seu comparador padrão. Não depende da ordem de HashSet/Dictionary nem altera a coleção do chamador. Assim [A, A, B], [A, B] e [B, A] produzem a mesma sequência para a mesma configuração.

A cópia é feita na chamada: alterações posteriores na coleção original não modificam aquela assinatura. Essa normalização é de instrumentos solicitados, não deduplicação de eventos de feed.

## Async, cancelamento e estado

As três operações públicas continuam explicitamente tipadas como IAsyncEnumerable<Quote/Trade/Candle>. Um iterator privado genérico compartilha somente o percurso finito; não cria object stream, MarketEvent ou hierarquia de eventos.

A produção acontece sob demanda em MoveNextAsync. Os dados in-memory já estão disponíveis; await Task.CompletedTask não representa espera de rede. Não há Task.Delay, produtor em background, buffering próprio, Channels ou Rx.

O iterator usa EnumeratorCancellation, respeitando tanto o token da operação quanto o token da enumeração. Quando distintos, a infraestrutura padrão do iterator combina os tokens. Cancelamento é verificado antes do primeiro dado, antes de cada observação e antes do encerramento normal. Token cancelado gera OperationCanceledException padrão, sem transformar cancelamento em stream vazio, resultado customizado ou retry.

Cada chamada e cada enumeração possuem contador local e assinatura própria. Repetir a mesma chamada ou enumerar novamente o mesmo stream reinicia as rodadas; enumerações intercaladas não compartilham progresso. Options e descriptor são imutáveis. Não há contador global, locking ou estado mutável compartilhado de progressão.

## Testes e regressão

Infrastructure.Tests referencia somente Infrastructure diretamente, com Application/Domain transitivos e os mesmos pacotes/versões de teste estabelecidos. A solution inclui esse projeto. Testes cobrem descriptor, configuração, assinaturas inválidas, um/vários instrumentos, normalização/cópia, sequências completas e finitas, repetição, interleaving, precisão/escala/extremos decimal, limites temporais e cancelamento por ambos os tokens, sem Task.Delay.

Não há normalização/formatação textual nova na configuração ou geração. A normalização do descriptor continua coberta pelos testes de Application; não são adicionados testes culturais artificiais nesta simulação.

## Limitações e decisões adiadas

Preços e quantidades são constantes, iguais entre instrumentos, quotes são locked e candles flat. Ordenação determinística é uma propriedade deste adapter, sem adicionar garantias universais ao contrato Application. Não há real-time pacing, inferência de finalização de candle, catálogo/increments, realismo financeiro ou garantias de feed externo.

O provider não usa rede, SDK, credenciais, PostgreSQL, Redis, sistema de arquivos ou clock real. Ficam adiados DI/orquestração, normalização de payloads externos, envelopes/sequence, deduplicação de feed, aggregators, order book, aggressor side, retry/reconnect, replay histórico, relógios de Replay, providers reais, persistência, API e frontend.

ADR-010 registra esta decisão com status Proposed para revisão arquitetural.
