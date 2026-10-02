# ADR-006 — Temporal Semantics

- Status: Accepted
- Date: 2026-10-01.
- Scope: Instantes canônicos e intervalos temporais antes de Market Data, sem persistência ou relógio de domínio.

## Context

Market Data, Replay e Backtesting precisam distinguir quando um evento ocorreu, quando foi recebido e quando um estágio o processou. Um Timestamp ambíguo, timezone implícito ou acesso ao relógio real no Domain dificultaria reprodução determinística e controle da informação disponível. A fundação de instrumentos está aprovada, mas Quote/Trade/Candle e consumidores de tempo ainda não existem.

## Decision

Definir UTC como representação interna canônica de instantes absolutos e DateTimeOffset como representação .NET subjacente. Introduzir UtcInstant em Common/Time como value object imutável que normaliza com ToUniversalTime, preservando todos os ticks suportados. Igualdade/hash usam o instante normalizado; CompareTo oferece ordenação natural. ToString usa formato O invariant, com offset zero explícito.

Definir EventTime como o instante de ocorrência na fonte/mercado informado pelo provider. Definir ReceivedTime como o instante em que o Vortex recebe/aceita o evento na fronteira de ingestão. EventTime != ReceivedTime em significado; esses campos não se substituem silenciosamente. Ausência de EventTime deverá ser explicitamente tratada na normalização futura, sem fallback definido agora.

ProcessingTime identifica o instante de um estágio interno que necessitar desse dado; não é obrigatório em todos os eventos e não recebe primitive separada. Não consultar Now/UtcNow dentro do Domain. Adiar abstração de relógio até um caso de uso consumidor.

Introduzir CandleInterval como value object imutável baseado em TimeSpan estritamente positivo, com duração exata, intervals nomeados e criação customizada. OneDay representa 24 horas; meses/trimestres/anos e intervalos de sessão exigem semântica futura própria. ToString usa formato c invariant.

Documentar limites futuros de candle como [OpenTime, CloseTime), início inclusivo e fim exclusivo. Open aceita atualizações; Closed é final para aquela fonte/processamento normal. Não implementar Candle ou CandleState sem consumidor.

Preservar EventTime histórico no Replay e distinguir esse dado do relógio virtual de execução. Backtesting deverá consumir somente informação disponível até seu instante virtual atual, permitindo evitar look-ahead bias. Disponibilidade não pode ser inferida somente de EventTime sem considerar a política futura de ingestão/reprodução.

## Consequences

- Offsets diferentes do mesmo instante convergem para valores UTC iguais, sem dependência do timezone da máquina.
- Nomes futuros EventTime/ReceivedTime/ProcessingTime expressam significado e origem; compartilhar UtcInstant não torna esses conceitos sinônimos.
- Instantes/durações preservam ticks sem antecipar precisão de provider ou PostgreSQL.
- Domain não cria tempo implicitamente; relógios reais/virtuais poderão ser introduzidos em fronteiras com consumidores claros.
- Duração fixa é distinta de calendário/sessão. Um intervalo de 24 horas não decide a semântica de candle diário de uma exchange.
- Eventos atrasados, fora de ordem ou duplicados continuam possíveis. UTC não resolve ordenação, sincronização de relógios ou reconciliação.
- Estado Closed não define política de revisão posterior; finalização e disponibilidade precisarão de requisitos explícitos antes de Replay/Backtesting.
- Não há mudança em persistência, pacotes, referências de projetos ou contratos de Application/API.

## Alternatives

DateTime como Timestamp genérico deixaria Kind/timezone e significado dependentes do chamador. DateTimeOffset sem normalização preservaria offsets distintos na representação interna. Epoch inteiro/string exigiria convenção de unidade/formato e mais regras de fronteira. A primitive normalizada dá semântica explícita sem parser ou dependência externa.

Truncar instantes/durações anteciparia uma política de precisão. Usar string livre para resolução ou enum fechado de intervalos impediria durações customizadas. TimeSpan para mês/ano confundiria calendário com duração fixa. Uma factory Now ou IClock sem consumidor ampliaria a implementação antes de um caso de uso legítimo.

## Deferred Decisions

Adiar timezone/sessões/calendários, resolução de datas locais ambíguas, formatos de providers, precisão/serialização/persistência, missing EventTime, sincronização de relógios, latência, ancoragem de candles, fechamento/revisões, buffering/watermarks, deduplicação, sequences, relógio virtual e motores de Replay/Backtesting. Quote/Trade/Candle, Trading e integrações externas permanecem fora desta tarefa.

## References

- ADR-005 — Market Instrument Identity.
- Temporal Foundation — TASK 0.8.
- [DateTimeOffset.ToUniversalTime](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.touniversaltime?view=net-10.0).
- [Formatos de DateTimeOffset](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings).
- [Formatos de TimeSpan](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-timespan-format-strings).
