# Temporal Foundation — TASK 0.8

Fundação temporal sobre `9e116f6`, anterior a Market Data, Trading, Replay e Backtesting. A implementação limita-se a UtcInstant e CandleInterval; as demais decisões abaixo estabelecem semântica para futuros consumidores.

## Representação canônica de instantes

UTC é a representação interna canônica. `UtcInstant`, em `Common/Time`, é um `sealed record` com `DateTimeOffset Value` get-only. Seu construtor aceita DateTimeOffset e chama `ToUniversalTime()`, preservando o instante e normalizando Offset para zero. Não existe entrada DateTime ou string, conversão para horário local, parser de provider ou dependência de timezone da máquina.

`2026-10-01T19:00:00-03:00` e `2026-10-01T22:00:00Z` produzem valores iguais. Equality, `==`/`!=` e hashing usam o instante normalizado, sem depender do offset original. `IComparable<UtcInstant>.CompareTo` oferece ordenação natural pelo instante, inclusive diferenças de um tick. Uma instância compara como maior que null, conforme a convenção de IComparable; não foram adicionados operadores de ordenação ou aritmética temporal.

Cada tick suportado por DateTimeOffset é preservado: não há truncamento para milissegundos, microssegundos ou precisão de banco. Toda a faixa válida de DateTimeOffset é aceita, incluindo MinValue/MaxValue; esses valores não representam ausência. Restrições de disponibilidade/validade de timestamps de uma fonte pertencerão à sua normalização futura.

`ToString()` usa formato `O` e InvariantCulture, por exemplo `2026-10-01T22:00:00.1234567+00:00`. O texto mantém sete casas fracionárias e offset UTC explícito. `+00:00` expressa o mesmo offset zero de `Z`. Não é um contrato de serialização ou decisão de coluna PostgreSQL.

## Relógio e determinismo

Domain não consulta o relógio do sistema. Não há Now/UtcNow, factory de instante atual, IClock ou TimeProvider adicionado. Os instantes chegam explicitamente como dados do chamador. A necessidade futura de abstração de relógio fica registrada para o primeiro caso de uso que realmente precise obter o instante atual, incluindo eventual relógio virtual de Replay/Backtesting.

## Significado de cada tempo

| Conceito futuro | Definição | Origem/contexto |
| --- | --- | --- |
| EventTime | Instante em que o evento ocorreu na fonte ou mercado, conforme informado pelo provider | Dado do provider, por exemplo execução de um Trade na exchange |
| ReceivedTime | Instante em que o Vortex recebeu ou aceitou o evento na fronteira de ingestão | Fronteira de ingestão do Vortex |
| ProcessingTime | Instante associado ao processamento interno de um estágio específico | Estágio ou observabilidade que precisar desse dado |

EventTime e ReceivedTime não são sinônimos. Exemplo: EventTime `14:30:00.100Z` e ReceivedTime `14:30:00.145Z` são instantes distintos, separados por 45 ms. Nenhum cálculo de latência é implementado nesta tarefa.

Market Data deverá preservar EventTime e ReceivedTime quando disponíveis, sem substituir silenciosamente um pelo outro. Se um provider não informar EventTime, essa limitação deverá ser explicitamente tratada na futura normalização; nenhum fallback é definido agora. ProcessingTime não é obrigatório em todo evento e não recebe primitive separada.

Os conceitos compartilham a representação de instante, mas futuros contratos deverão usar nomes explícitos para cada papel. UTC não elimina diferenças de qualidade/sincronização entre relógios das fontes e da ingestão. Não se introduz nesta etapa uma invariante de ordenação entre esses campos.

## Horário local da exchange

Representação UTC interna não implica operação das exchanges em UTC. Uma fonte pode informar horário local com offset ou depender de contexto de timezone. A fronteira futura de normalização deverá interpretar esse contexto explicitamente antes de produzir UtcInstant.

Offset não é identificador de timezone nem calendário de sessão. Venue permanece inalterado: timezone, horário de verão, sessões e calendários serão modelados quando houver requisitos. Não há inferência pelo timezone da máquina.

## CandleInterval e limites temporais

`CandleInterval` é um `sealed record` com `TimeSpan Duration` get-only. O construtor exige duração estritamente positiva e lança DomainException para zero/negativo. Preserva todos os ticks, aceita durações customizadas e fornece igualdade/hash por duração.

Propriedades nomeadas: OneMinute, FiveMinutes, FifteenMinutes, ThirtyMinutes, OneHour, FourHours e OneDay. OneDay significa exatamente 24 horas de duração; não define um candle diário de sessão nem um dia civil em timezone local. Meses, trimestres e anos são intervalos de calendário e não são modelados como TimeSpan.

`ToString()` usa formato constante `c` e InvariantCulture, por exemplo `1.00:00:00.1234567`. Não circulam strings livres de resolução dentro do Domain, e não há restrição a um conjunto fechado de intervalos.

Um candle temporal futuro será associado a `[OpenTime, CloseTime)`: OpenTime inclusivo e CloseTime exclusivo. Para o intervalo de um minuto iniciado às `10:00:00Z`, pertencem eventos com `10:00:00Z <= event < 10:01:00Z`. O evento exatamente às `10:01:00Z` fica fora daquele intervalo. A duração não define sozinha o ponto de ancoragem dos limites; alinhamento, sessões e regras do provider ficam adiados. Nenhum Candle ou algoritmo de agregação é implementado.

## Estado e chegada de eventos

Open significa que o candle ainda pode receber atualizações. Closed significa que é final para aquela fonte/processamento normal. Essa definição não antecipa políticas de correção ou revisão posteriores. CandleState permanece apenas conceitual, sem enum ou consumidor implementado.

Eventos podem chegar atrasados, fora de ordem ou duplicados. Ordem por ReceivedTime não equivale a ordem por EventTime; timestamps iguais também não fornecem desempate universal. Não há buffering, watermark, deduplicação, correção de candle ou política de finalização nesta tarefa.

Providers poderão fornecer sequence/provider sequence/exchange sequence, com escopo próprio, para ordenação ou reconciliação. Não é criada uma sequência universal, nem um wrapper para esses dados.

## Replay, Backtesting e disponibilidade

Replay deverá preservar o EventTime histórico dos dados. O relógio virtual controla o avanço da execução e não substitui o instante original do evento: um EventTime `2025-01-10T14:30:00Z` continua sendo esse instante histórico durante a reprodução.

Backtesting deverá consumir somente informação disponível até o instante virtual atual. Um EventTime anterior, por si só, não prova que o evento ou uma revisão de candle já estavam disponíveis naquele ponto da simulação. A futura política de reprodução deverá considerar disponibilidade e origem dos dados, eventos atrasados e o estado de finalização, para permitir evitar look-ahead bias. Não se define agora fallback de ReceivedTime, algoritmo de ordenação ou motor de simulação.

## Verificação e decisões adiadas

Testes novos cobrem normalização de offsets positivos/negativos, equivalência, hashing, comparação, preservação de ticks, limites de representação, intervalos padrão/customizados e duração inválida. Formatação é verificada sob tr-TR, pt-BR e ar-SA, com restauração de cultura em finally e round-trip sem perda de ticks. Não há relógio real nos testes temporais.

Domain continua sem dependências externas. Os 131 testes anteriores, projetos, solution, DbContext, API/Application/Infrastructure, frontend e infraestrutura permanecem inalterados. PostgreSQL/Redis não são usados pelos testes ou modelo.

Adiam-se relógio com consumidor, parsers/providers, precisão nas fronteiras, representação relacional, timezone/sessões, intervalos de calendário, Candle/CandleState, finalização/correções, ordenação/deduplicação, sequence, Replay e Backtesting. ADR-006 registra estas decisões como Proposed.

Referências: [DateTimeOffset.ToUniversalTime](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.touniversaltime?view=net-10.0), [formato round-trip O](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings#the-round-trip-o-o-format-specifier), [formato constante TimeSpan c](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-timespan-format-strings#the-constant-c-format-specifier).
