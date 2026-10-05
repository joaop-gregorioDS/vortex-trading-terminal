# ADR-031 — Minimal Historical Chart Vertical Slice

- Status: Accepted
- Date: 2026-10-05
- Scope: TASK 0.36

## Context

A fundação matemática e as fronteiras de consumo/replay estão consolidadas. A primeira tela precisa demonstrar seu consumo end-to-end sem alterar os componentes congelados nem introduzir estado incremental, integração de mercado real ou dependência de bancos disponíveis. O design da TASK 0.35 foi aprovado para uma demonstração limitada a Candles, SMA(20) e MACD(12,26,9).

## Decision

Entregar um snapshot histórico estático de desenvolvimento, com exatamente 200 Candles determinísticos de um minuto, contexto/âncora explícitos e identificação de demo. Infrastructure alimenta o HistoricalReplayProvider existente; a composição registra o provider e mapeia o endpoint somente em Development, sem fallback implícito em Production.

Uma pequena HistoricalChartQuery em Application depende de IMarketDataProvider, obtém os primeiros N Candles, propaga cancellation e calcula todos os indicadores pelo IndicatorBatchCalculator sobre o mesmo batch retornado. Não usa CausalIndicatorReplayRuntime ou cursor de replay.

O endpoint composto POST /api/chart-snapshots usa DTOs próprios, IDs estáveis `sma`/`macd`, parâmetros tipados e tradução explícita. Transporte/recursos são validados fora da engine; construtores congelados preservam autoridade sobre matemática. Limites: corpo 8192 bytes, MaxBars 1..200 (default 200), uma ou duas solicitações de indicador e períodos com teto 200. Dados internos inválidos resultam em 500, distinguindo erros do cliente e indisponibilidade da fonte.

OHLCV e IndicatorPoint.Value são strings decimais invariantes, sem double ou rounding. Instantes são ISO-8601 UTC com precisão de ticks. CloseTime posiciona Candles; IndicatorPoint.Time posiciona cada série, sem padding ou alinhamento por índice.

Next.js busca dados no Server Component em tempo de requisição, sem depender da API durante o build. O Client Component cria/descarta Lightweight Charts 5.2.1, única nova dependência direta de visualização, com panes nativos e atribuição preservada. Um adaptador privado ao frontend converte strings decimais para number e UTC para segundos Unix somente para desenho, rejeitando tempos incompatíveis com a fixture em minutos completos e colisões.

Não adicionar SignalR, replay animado, live provider, histórico real ou cache. Não modificar Domain, Indicator Engine, Consumption Boundary, Causal Replay Runtime, persistência ou readiness checks. PostgreSQL/Redis podem estar indisponíveis para essa demonstração. F-05 continua dívida de tooling aceita, sem alteração da cadeia vulnerável nem vulnerabilidades de produção introduzidas pelo chart.

## Consequences

A interface passa a demonstrar o motor matemático real com isolamento de precisão: backend decimal autoritativo e aproximação confinada ao desenho. Uma requisição de 200 barras produz 181 pontos SMA, 175 MACD e 167 Signal/Histogram. O snapshot não oferece causalidade progressiva; o histórico demo não representa mercado real e o endpoint não é disponibilizado em Production.

Os limites mantêm a fatia pequena e testável, sem registro genérico de indicadores ou abstrações prematuras de cache/streaming. Contratos futuros para dados reais, replay ou outros IDs exigem decisões específicas. A UI contém estados loading/erro/vazio e resize responsivo, mas não constitui um terminal completo.

## Alternatives

- Usar runtime causal: acrescentaria cursor/estado a um snapshot estático; reservado à experiência progressiva futura.
- Calcular no frontend ou transportar números double: perderia autoridade decimal e duplicaria matemática congelada.
- Serializar resultados internos diretamente: exporia tipos e acoplamento de Application ao contrato externo.
- Fetch durante o build: exigiria backend disponível para compilar o frontend.
- Wrapper React ou outra chart library: dependência adicional sem necessidade para a composição de dois panes.

## Validation

Testes de query, fixture/provider, mapping/HTTP e adaptador verificam mesmo batch, limites, counts, nomes, warm-up, precisão, UTC, erros seguros e falhas internas. A regressão existente permanece intacta. Smoke Development verifica renderização/resize sem iniciar PostgreSQL/Redis. A documentação detalhada está em [Historical Chart Vertical Slice](../architecture/historical-chart-vertical-slice.md).

Este ADR permanece **Proposed** até revisão arquitetural e autorização específica de checkpoint.
