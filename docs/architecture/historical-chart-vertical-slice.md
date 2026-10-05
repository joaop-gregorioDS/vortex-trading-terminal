# Minimal Historical Chart Vertical Slice

TASK 0.36 — 2026-10-05. Decisão registrada em [ADR-031](../adr/ADR-031-minimal-historical-chart-vertical-slice.md), inicialmente **Proposed**.

## Fluxo e semântica

```text
DevelopmentCandleDataset → HistoricalReplayProvider → HistoricalChartQuery
→ IndicatorBatchCalculator → API DTOs → Next Server Component → Client chart
```

O caso de uso entrega um snapshot histórico estático, com os primeiros N Candles da âncora da fixture. Não representa execução progressiva, prevenção de look-ahead durante playback, cursor ou relógio de replay. Domain, Indicator Engine, Indicator Consumption Boundary e Causal Replay Runtime permanecem congelados, sem alterações.

## Fonte de desenvolvimento

`DevelopmentCandleDataset`, em Infrastructure, constrói exatamente 200 Candles em `decimal`:

- InstrumentId: `11111111-1111-1111-1111-111111111111`.
- Intervalo: `1m`.
- Primeiro OpenTime: `2026-10-01T10:00:00.0000000Z`.
- SourceId: `development-history-v1`; `isDemo = true`.

Para índice `i` de 0 a 199, Close é `100 + (i % 23 - 11) × 0.32 + i × 0.015 + (i % 5) × 0.075`. Open usa o Close anterior, inicialmente 100. High é `max(Open, Close) + 0.35 + (i % 4) × 0.05`; Low é `min(Open, Close) - 0.30 - (i % 3) × 0.04`; Volume é `1000 + (i % 17) × 37 + i × 0.5`. OpenTime avança um minuto e CloseTime fica um minuto depois.

Não há Random, relógio atual, arquivos, rede ou banco de dados. Os construtores existentes validam os Candles. A sequência contém movimentos positivos e negativos e volume variável. Os eventos alimentam o `HistoricalReplayProvider` existente, sem alterar seu comportamento ou criar outro provider.

A composição registra contexto e provider exclusivamente em `IsDevelopment()`. O endpoint também só é mapeado nesse ambiente. Em Production o caminho de demonstração não existe; não há fallback para mercado fictício.

## Application

`HistoricalChartQuery` depende de `IMarketDataProvider` e de um pequeno contexto tipado. Request e resultado são internos e não contêm HTTP, JSON ou tipos de biblioteca gráfica.

A query valida contexto e recursos, enumera no máximo N barras, propaga cancellation e cria um batch somente leitura com as mesmas referências dos Candles recebidos. Para cada specification, chama o `IndicatorBatchCalculator` sobre esse exato batch; o mesmo batch é retornado no snapshot. Não existe histórico oculto ou cálculo alternativo dos indicadores.

Para 200 barras, SMA(20) contém 181 pontos; MACD(12,26,9) contém 175 pontos na série MACD e 167 em Signal e Histogram. Os nomes e a ordem são preservados: `Value`, ou `MACD`, `Signal`, `Histogram`. Não há padding, zero-fill ou alinhamento por índice.

## API e propriedade da validação

Único novo endpoint: `POST /api/chart-snapshots`, em Development.

```json
{
  "instrumentId": "11111111-1111-1111-1111-111111111111",
  "interval": "1m",
  "maxBars": 200,
  "indicators": [
    { "id": "sma", "parameters": { "period": 20 } },
    { "id": "macd", "parameters": { "fastPeriod": 12, "slowPeriod": 26, "signalPeriod": 9 } }
  ]
}
```

MaxBars tem default de transporte 200. Os DTOs específicos da API usam formas tipadas e discriminadores estáveis `sma`/`macd`. Um switch explícito traduz para specifications internas; os nomes CLR não entram no JSON. A resposta também usa DTOs próprios.

Campos obrigatórios, representação de GUID/int, token de intervalo, ID e forma dos parâmetros pertencem ao transporte. As políticas de trabalho pertencem à query: MaxBars 1..200, uma ou duas solicitações de indicador e teto superior 200 para cada período. A positividade dos períodos e a ordem fast/slow pertencem exclusivamente aos construtores congelados. O transporte reconhece `5m`, mas esse contexto não está disponível e recebe 404; tokens desconhecidos recebem 400.

O corpo é limitado a 8192 bytes, inclusive sem Content-Length. O parser é case-sensitive, rejeita propriedades não previstas, limita profundidade a 8 e aceita `id` em posições distintas dentro do objeto. Não há dispatch por reflection, dynamic, Activator ou JsonElement.

| Falha | HTTP |
| --- | --- |
| Transporte inválido, ID/forma não suportados ou limite de recursos | 400 |
| Corpo acima de 8192 bytes | 413 |
| Contexto histórico inexistente/sem barras | 404 |
| Configuração matematicamente inválida | 422 |
| Overflow atribuível ao cálculo | 422 |
| Fonte indisponível por falha de I/O | 503 |
| Sequência interna inválida ou falha inesperada | 500 |

Uma `ArgumentException` sobre `candles` não é convertida em erro do cliente: passa ao tratamento 500 existente. A query também rejeita barras nulas ou de contexto diferente sem reparar dados. ProblemDetails usa títulos/códigos seguros e traceId, sem mensagem interna, stack trace, path ou nome CLR. O logging do novo endpoint contém somente contexto, intervalo permitido, contagens, duração, status, categoria e traceId; não contém bodies ou arrays.

## Precisão e tempo no transporte

A resposta contém context (`instrumentId`, `interval`, `sourceId`, `isDemo`, `barCount`), Candles (`openTime`, `closeTime`, OHLCV) e indicadores (`id`, parâmetros tipados, séries e pontos).

Todos os valores financeiros autoritativos são strings JSON produzidas diretamente por `decimal.ToString(InvariantCulture)`: Open, High, Low, Close, Volume e cada IndicatorPoint.Value. Não há arredondamento, conversão para double ou escala de biblioteca gráfica no backend. Parâmetros desta etapa são inteiros; parâmetros decimais futuros deverão manter a política de strings, sem serem implementados aqui.

Instantes são strings UTC `yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'`, preservando os sete dígitos de precisão de ticks. Candle e indicador se posicionam por CloseTime/IndicatorPoint.Time. O backend não transporta Unix timestamps.

## Independência de infraestrutura

A obtenção do snapshot não acessa PostgreSQL ou Redis e não exige disponibilidade desses serviços. O startup ainda respeita a configuração de conexões exigida pela fundação de persistência. `/health/ready` e seus checks ficam intactos: readiness e disponibilidade funcional desse endpoint têm significados diferentes.

## Frontend e adaptador

A página é um Server Component. `await connection()` adia a busca até a requisição; `fetch` usa `no-store`, POST e timeout de cinco segundos. O build funciona sem backend. `VORTEX_API_BASE_URL` é configuração exclusiva do servidor; somente em desenvolvimento há fallback local `http://127.0.0.1:5187`. Ausência de configuração em produção não seleciona demo implicitamente.

O Client Component importa Lightweight Charts dinamicamente no efeito, cria um único chart, usa panes nativos v5, ResizeObserver via `autoSize` e remove o chart no cleanup. Candle e SMA ficam no pane principal; Histogram, MACD e Signal no pane inferior. A legenda identifica a demonstração e os indicadores. Há loading, erro sanitizado/retry e estado vazio; a altura é responsiva e o conteúdo não exige largura fixa.

O adaptador é a única fronteira de conversão para `number` e segundos Unix. Ele preserva o objeto de transporte e suas strings, rejeita formato decimal inválido, valores não finitos e underflow de valor não zero. Valida UTC canônico em minutos inteiros, round-trip de calendário, ordenação estrita e ausência de colisões, sem deduplicar ou truncar precisão silenciosamente. Essa fixture aceita somente minutos completos; subsegundos são rejeitados. Os pontos devem pertencer aos CloseTimes do batch e cada série conserva seus próprios timestamps e warm-up.

Os números convertidos são aproximações para desenho; nunca retornam como valores financeiros autoritativos. Não há SMA/MACD implementados no navegador.

## Dependência e atribuição

Única nova dependência direta de visualização: `lightweight-charts@5.2.1`, Apache-2.0. A única nova transitiva é `fancy-canvas@2.1.0`. Não foi instalado wrapper React ou framework de testes. A logo padrão de atribuição fica habilitada; o rodapé inclui ligação à TradingView e o NOTICE da release:

> TradingView Lightweight Charts™
> Copyright (с) 2025 TradingView, Inc. https://www.tradingview.com/

Referências oficiais: [documentação v5.2](https://tradingview.github.io/lightweight-charts/docs), [LICENSE v5.2.1](https://github.com/tradingview/lightweight-charts/blob/v5.2.1/LICENSE), [NOTICE v5.2.1](https://github.com/tradingview/lightweight-charts/blob/v5.2.1/NOTICE).

F-05 permanece dívida aceita de tooling: cinco pacotes afetados na cadeia ESLint pelo advisory `GHSA-vfj7-8cjw-p6xm`, inalterados com a instalação. Audit de produção permanece em zero. Nenhum audit fix, override, downgrade ou upgrade não relacionado foi feito.

## Validação e limites

Regressão backend: 1674 testes aprovados, zero falhas/ignorados; 57 novos em query, fixture/provider e transporte HTTP. Build sem erros/warnings. Adaptador: seis testes via runner nativo Node, sem framework adicional; o Node pode emitir aviso de formato de módulo nessa execução de testes. Lint e build de produção aprovados sem API em execução.

Smoke em Development confirmou 200 Candles, SMA e pane MACD, console sem erros e erro seguro com a API indisponível. Em viewport de 390 px, a verificação do DOM indicou resize sem overflow horizontal; a captura móvel salva ficou inconsistente e não certifica a aparência móvel. Uma tentativa posterior de recaptura foi bloqueada pela política de URL da ferramenta de navegador, sem contorno do bloqueio. A captura desktop é anterior à inclusão final do aviso de copyright no rodapé. Nenhum PostgreSQL/Redis foi iniciado. O smoke não certifica streaming, ambiente Production ou todos os gestos gráficos.

O trabalho é limitado por 200 barras, dois indicadores e períodos até 200. Não há cache ou benchmark pesado. A fórmula e o custo dos indicadores congelados permanecem intactos.

Ficam adiados: replay animado, SignalR, live provider, história real, trading, orders, positions, desenho, indicator picker, workspace, cache, MarketSession, TradingCalendar, backtesting, Strategy Engine e robots. ADR-031 permanece Proposed; a implementação não autoriza checkpoint, commit ou push.
