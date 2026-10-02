# Market Data Core — TASK 0.9

Modelos canônicos sobre `0a2ca75`: Quote e Candle usam as primitives aprovadas, sem provider, ingestão, streaming, persistência ou agregação. São snapshots explícitos com semânticas próprias.

## Representação e igualdade

Ambos são `sealed record` com propriedades get-only, construção validada e igualdade/hash estruturais. Não possuem identidade própria QuoteId/CandleId, setters ou lifecycle. Não há base MarketEvent, Entity ou herança entre os dois modelos.

Quote compara InstrumentId, EventTime, ReceivedTime e os quatro componentes bid/ask. Candle compara InstrumentId, Interval, OpenTime, OHLC e Volume. Valores equivalentes das primitives são iguais, incluindo decimals com escalas representacionais diferentes e instantes originalmente informados com offsets diferentes. Igualdade de snapshot não define deduplicação de provider, chave de catálogo ou identidade de uma revisão.

## Quote

Quote representa o snapshot do melhor bid/ask conhecido de um instrumento em EventTime. Exige os dois lados completos:

```text
InstrumentId
EventTime / ReceivedTime
BidPrice / BidQuantity
AskPrice / AskQuantity
```

Todos os componentes são obrigatórios; null gera DomainException. BidPrice e AskPrice devem ser estritamente positivos, com BidPrice <= AskPrice. Bid < Ask representa spread normal; Bid == Ask é locked e permitido; Bid > Ask é crossed e rejeitado nesta representação inicial. Não há cálculo de spread ou convenção específica de exchange.

BidQuantity/AskQuantity usam Quantity e aceitam zero e frações. Zero não remove o lado, não indica ausência e não cria um evento especial. Bid-only/ask-only não são representados. Dados de fonte que não se encaixem no contrato deverão receber tratamento explícito na futura ingestão.

EventTime expressa quando o evento ocorreu na fonte/mercado, conforme o provider. ReceivedTime expressa quando entrou na fronteira Vortex. Ambos são UtcInstant obrigatórios e são preservados, incluindo ticks, sem fallback ou invariante ReceivedTime >= EventTime. Uma recepção anterior ao EventTime é aceita; o snapshot não presume relógios sincronizados.

## Candle

Candle é um snapshot canônico OHLCV de um intervalo temporal. Exige:

```text
InstrumentId / Interval / OpenTime
Open / High / Low / Close
Volume
```

Todos os componentes são obrigatórios; null gera DomainException. OHLC usa Price e exige valores estritamente positivos. As invariantes são:

```text
High >= Open
High >= Close
Low <= Open
Low <= Close
High >= Low
```

Isso mantém Open e Close dentro do range Low/High. Candles de alta, baixa e flat (Open == High == Low == Close) são válidos. Não se infere ordem intraintervalo de High/Low nem trajetória de preços a partir de OHLC.

Volume usa Quantity, permite zero e frações e não impõe lot size. Um snapshot válido fornecido por fonte/dataset pode ter volume zero; nenhuma regra de preenchimento ou geração de candles vazios é implementada.

## Temporalidade de Candle

OpenTime é um UtcInstant e Interval é um CandleInterval positivo, incluindo durações customizadas. CloseTime é propriedade calculada como `new UtcInstant(OpenTime.Value.Add(Interval.Duration))`; não é armazenado em campo independente ou recebido do chamador.

A construção verifica ticks restantes até DateTimeOffset.MaxValue antes da adição: `Interval.Duration.Ticks > DateTimeOffset.MaxValue.UtcTicks - OpenTime.Value.UtcTicks` gera DomainException. A subtração de ticks válidos não excede long, e a verificação evita tanto extrapolação de DateTimeOffset quanto overflow de uma soma intermediária. Um resultado exatamente no limite máximo é permitido. Todo Candle construído possui CloseTime representável e preciso, sem truncamento ou saturação.

Os limites conceituais permanecem `[OpenTime, CloseTime)`: início inclusivo e fim exclusivo. Exemplo: OpenTime 10:00Z com intervalo de um minuto cobre eventos anteriores a 10:01Z; o evento exatamente às 10:01Z fica fora desse intervalo. Não há alinhamento automático a relógio/calendário, método de agregação ou teste de pertencimento de eventos no modelo.

Candle não recebe ReceivedTime nesta etapa: sua temporalidade é a do intervalo agregado, distinta da observação bid/ask de Quote. Também não possui CandleState. O snapshot, isoladamente, não informa se está em formação ou é final; essa informação deverá entrar com pipeline/agregação/streaming e consumidor real. Não presumir que todo snapshot é closed para uso em Backtesting.

## Incrementos e precisão

Os dois modelos referenciam apenas InstrumentId, sem carregar Instrument ou consultar catálogo. Tick size e quantity increment dependem de metadata externa; a validação ocorrerá em uma fronteira futura com acesso a essa informação. Construção de um snapshot estruturalmente válido não comprova alinhamento ao instrumento nem disponibilidade de negociação.

Price/Quantity/UtcInstant/CandleInterval existentes permanecem inalterados. Decimals e suas escalas são preservados sem Round/Floor/Ceiling, float/double, epsilon ou tolerância. Comparações usam diretamente decimal, sem calcular diferenças financeiras que poderiam ultrapassar seu range. Os testes incluem valores de 28 casas, decimal.MaxValue e preservação via decimal.GetBits.

## Testes e fronteiras

QuoteTests cobre construção, sete componentes ausentes, preços zero, normal/locked/crossed, quantidades zero/fracionárias, tempos anterior/igual/posterior, igualdade/hash, precisão e ausência de validação de increments no snapshot. CandleTests cobre oito componentes ausentes, cada preço zero, ranges impossíveis, flat, volume, duração precisa, overflow explícito, limite máximo, igualdade/hash e ausência de validação de increments.

Nenhum teste novo consulta relógio real, banco, Redis ou rede. Não há pacote novo ou alteração em projetos/solution, Application/API/Infrastructure, frontend ou infraestrutura. Domain continua sem dependências externas.

## Decisões adiadas

Trade terá tarefa própria para preço/quantidade, EventTime/ReceivedTime, identidade de fonte, aggressor side, sequence e duplicatas. Também ficam adiados providers/interfaces, política para dados incompatíveis com o contrato, catálogo e increment validation, order book, agregação, CandleState, finalização/revisões, disponibilidade temporal, deduplicação, sequences, Replay/Backtesting, relógio, persistência, APIs e transporte em tempo real.

A pequena repetição de InstrumentId entre os modelos não justifica uma base MarketEvent: Quote e Candle não compartilham a mesma temporalidade nem invariantes. ADR-007 registra o modelo para revisão arquitetural.
