# ADR-046: Pregão pelo Yahoo Finance e parada no encerramento

## Status
Aceito

## Contexto
O pregão da TASK 11.0 andava por um caminho de preço determinístico (`SimulatedPricePath`). A conta de demonstração precisa acompanhar o pregão real com a série gratuita do Yahoo Finance, a mesma já usada no histórico. Essa série não é o book da B3 e chega no horário que o Yahoo informa. O catálogo mistura ações e proxies de mini contrato (`^BVSP`, `BRL=X`), e o dólar comercial seguiria negociando depois do fechamento da B3.

## Decisão
1. **Um round sincronizado.** `YahooMarketFeed` lê o gráfico de 1 minuto de todo o catálogo na mesma passagem e entrega um `MarketRound` à sessão. Não há caminho de preço simulado nesse feed.
2. **O pregão é o da B3.** A sessão fica aberta só quando PETR4 informa `marketState` REGULAR e o relógio ainda está dentro de `currentTradingPeriod.regular`. Sem esse estado, vale a janela de dias úteis, 10:00 inclusive até 17:00 exclusive, em America/Sao_Paulo. Proxies param junto.
3. **O tape congela no encerramento.** O primeiro round fechado grava as barras da sessão e não casa ordens novas se o pregão já estava fechado. Rounds seguintes, enquanto continuar fechado, não mudam preço, gráfico nem livro. Na reabertura, o primeiro round só alinha o relógio. O print seguinte é que pode executar.
4. **A execução continua em paper.** O livro segue com a liquidez do perfil. O gráfico e a cotação do dia usam as barras OHLC do Yahoo, não um candle pintado. Valores seguem string decimal.
5. **O gráfico lê a fita de um minuto dos 15 ativos da observação.** Cada candle abre no horário de formação (15 minutos em :00, :15, :30 e :45 de Brasília). O candle em formação acompanha a cotação atrasada. Os minutos já recebidos ficam em cache e a variação deles é revelada ao longo do atraso, para o pavio crescer em vez de aparecer fechado. A boleta continua na cotação oficial. As sessões de 10:00 às 17:00 ficam; o overnight do câmbio sai. Esses minutos são gravados em `trading.minute_bars` e recarregados na subida. Sem o Postgres a fita continua na memória.

## Consequências
- Cotação, gráfico, livro, boleta e relatórios leem a mesma sessão. Fora do pregão a movimentação para.
- A série gratuita acompanha o horário do Yahoo. Não é um feed direto da B3.
- Continuam fora deste corte: dado contratado em tempo real, book de ofertas da bolsa, robô, stop, OCO e autenticação.
