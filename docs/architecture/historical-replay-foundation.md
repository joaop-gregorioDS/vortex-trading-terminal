# Historical Replay Foundation — TASK 0.14

## Purpose and Placement

Esta fundação define um dataset histórico lógico em memória e a posição temporal virtual de uma futura reprodução. ReplayEvent, ReplayEventKind, HistoricalReplayDataset e ReplayTimeline pertencem a VortexTrading.Application/MarketData/Replay, sem dependência de Infrastructure.

Replay coordena modelos canônicos para casos de uso da aplicação. Quote, Trade e Candle permanecem em Domain, independentes do mecanismo de reprodução. Importação e armazenamento não definem a semântica desta fundação.

## Replay Event

ReplayEvent é uma classe sealed imutável com três factories tipadas: FromQuote, FromTrade e FromCandle. Cada factory chama um construtor privado que exige um payload não nulo. Exatamente uma das propriedades Quote, Trade ou Candle contém o objeto original; as outras são null. Não há construtor público, setters, payload object público ou mecanismo para misturar tipos.

Kind, InstrumentId e ReplayTime são derivados do payload. Nenhum ID ou timestamp independente pode divergir do modelo original. A classe é um envelope de coordenação restrito a Replay em Application; não é uma hierarquia de eventos do Domain, identidade de evento ou envelope de provider.

## ReplayTime and Candle Semantics

| Payload | ReplayTime |
|---|---|
| Quote | Quote.EventTime |
| Trade | Trade.EventTime |
| Candle | Candle.OpenTime |

ReceivedTime não participa da ordenação nem do avanço virtual e permanece intacto, mesmo quando difere de EventTime. Nenhum timestamp é recalculado ou substituído pela timeline.

Candle usa explicitamente OpenTime, não CloseTime. EndTime do dataset pode ser anterior ao CloseTime de um candle. Esta escolha define a posição temporal do registro histórico; não declara que os valores completos de OHLC/Volume já eram conhecidos na abertura. Políticas de disponibilidade e prevenção de look-ahead para consumers de candles fechados exigirão uma decisão posterior. Esta tarefa não publica candles nem executa estratégias.

## Dataset Validation and Immutability

HistoricalReplayDataset recebe IReadOnlyCollection<ReplayEvent> e materializa um snapshot próprio. Coleção null causa ArgumentNullException para events; coleção vazia ou com evento null causa ArgumentException para events. Dataset vazio não é válido.

O snapshot é ordenado sem alterar a coleção original. Events expõe IReadOnlyList<ReplayEvent> sobre um array privado protegido por coleção somente de leitura. Não é possível substituir, adicionar ou remover eventos através da API exposta. Alterações posteriores na coleção do chamador não alteram Events, Count, StartTime ou EndTime. A coleção deve permanecer estável durante a própria construção; não há cópia concorrente coordenada.

Os envelopes e os payloads originais são preservados por referência. Não há clonagem, mutação, reescrita financeira ou alteração de timestamps.

## Canonical Ordering and Ties

A ordem canônica é ascendente pelos quatro critérios:

1. ReplayTime, comparado como UtcInstant, com precisão de ticks.
2. InstrumentId.Value, com o comparador padrão de Guid.
3. ReplayEventKind, com valores explícitos Quote = 0, Trade = 1 e Candle = 2.
4. Ordinal original de entrada, capturado durante o snapshot e usado exclusivamente como desempate final.

A ordem de kinds é mecânica e não representa prioridade financeira. O ordinal permanece interno à construção: não entra em Domain, não modifica payloads e não representa identidade ou sequence de exchange/provider.

Entradas com os mesmos eventos e a mesma ordem original produzem a mesma ordem canônica. Para eventos com os três primeiros critérios iguais, alterar a ordem de entrada altera intencionalmente o desempate final. Valores financeiros e ReceivedTime não participam desse desempate. Não há aleatoriedade, hash de runtime ou ordem incidental de Dictionary/HashSet.

## Duplicate Occurrences and Metadata

Todas as ocorrências são retidas, incluindo o mesmo envelope repetido, dois envelopes sobre o mesmo payload e payloads distintos estruturalmente iguais. Não há deduplicação histórica. A normalização de assinaturas do orchestrator é uma responsabilidade separada.

Count é o número de ocorrências; StartTime é o menor ReplayTime e EndTime é o maior. Um único evento ou vários eventos no mesmo instante produzem StartTime = EndTime. Não há metadata de origem, importação, armazenamento ou relógio real.

## Virtual Timeline

ReplayTimeline é uma timeline virtual genérica, limitada e monotônica em Application. Desde a TASK 0.18-A, pode receber diretamente UtcInstant startTime e endTime, sem nullable na assinatura. Referências nulas em runtime causam ArgumentNullException para o respectivo parâmetro; endTime anterior a startTime causa ArgumentOutOfRangeException para endTime. Limites iguais são válidos.

O construtor explícito preserva as instâncias fornecidas: StartTime = startTime, EndTime = endTime e CurrentTime = startTime. Os limites são imutáveis. A timeline não conhece ReplayEvent, kinds de Market Data, ocorrência, disponibilidade, providers ou Backtesting; não calcula seus próprios limites.

O construtor existente por HistoricalReplayDataset permanece com a mesma API e comportamento. Dataset null continua causando ArgumentNullException para dataset. Ele delega à inicialização por limites explícitos com dataset.StartTime e dataset.EndTime, mantendo CurrentTime em dataset.StartTime e preservando referências. O dataset é apenas uma fonte possível dos limites.

A timeline não retém cursor, enumera eventos ou controla entregas. Seu proprietário a avança explicitamente. ReplaySession permanece inalterada e continua usando o construtor por dataset para reprodução por ocorrência.

AdvanceTo exige UtcInstant não nulo. O range é fechado: StartTime <= instant <= EndTime. Valores fora desse range causam ArgumentOutOfRangeException para instant. Dentro do range, instant < CurrentTime também causa ArgumentOutOfRangeException. Rejeições preservam a posição anterior.

instant > CurrentTime avança a posição; instant = CurrentTime é permitido e não altera a posição. Isso permite processar várias ocorrências no mesmo timestamp. EndTime é aceito exatamente, e limites iguais permitem somente aquela posição. A operação compara instantes sem calcular durações ou adicionar ticks, aceitando todo o range de UtcInstant.

A timeline possui estado por sessão e deve ser avançada serialmente pelo seu proprietário. Não há estado global, sincronização para writers concorrentes, reset, seek ou rewind. Uma nova timeline começa novamente em StartTime sem modificar timelines existentes.

A TASK 0.18 revelou que o construtor exclusivamente por dataset não representava um horizonte de disponibilidade posterior a dataset.EndTime. Limites explícitos permitem uma futura execução fornecer minimum AvailabilityTime e maximum AvailabilityTime, sem alterar o dataset e sem acoplar a timeline à política. Por exemplo, um range explícito 10:03 → 10:05 aceita AdvanceTo(10:05), independentemente de um horizonte histórico de ocorrência 10:04. Esta generalização não implementa CausalReplaySession nem ordenação causal. ADR-016 registra a decisão como Proposed até revisão.

## Wall-Clock Independence

Tempo virtual avança apenas por AdvanceTo. Nenhum conceito desta fundação usa Now/UtcNow, TimeProvider.System, Stopwatch, Task.Delay, Thread.Sleep, timers ou velocidade de playback. Velocidade da CPU e tempo de parede não determinam a posição virtual.

O dataset expõe uma lista síncrona imutável. Não há IAsyncEnumerable nesta fundação, pois a exposição assíncrona será responsabilidade de um futuro provider. Materializar eventos durante a construção do dataset histórico é parte de seu snapshot, sem alterar o forwarding lazy do orchestrator existente.

## Deferred Decisions and Boundaries

Não há HistoricalReplayProvider, formato CSV/JSON/Parquet, importer, filesystem, schema, banco, Redis, persistência, DI, API, SignalR, frontend ou provider registry. Domain, provider contracts, orchestrator e Simulation provider permanecem inalterados.

Adiar implementação do provider, sessões/consumers, disponibilidade de candles fechados e look-ahead, transporte assíncrono, cancellation da reprodução, importação/persistência, seek/rewind/reset, política de pacing e velocidades, clocks consultáveis por estratégias, identidade/sequence e qualquer deduplicação de feed.

ADR-012 permanece Proposed até revisão arquitetural. Esta tarefa não autoriza staging ou commit.
