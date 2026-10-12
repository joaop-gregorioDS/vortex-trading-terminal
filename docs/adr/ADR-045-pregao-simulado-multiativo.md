# ADR-045: Pregão simulado multiativo e conta de demonstração

## Status
Aceito

## Contexto
A estação (ADR-044) passou a mostrar cotações, gráfico, livro, boleta, posições e relatórios ao mesmo tempo. Enquanto cada tela lia um estado próprio, o visitante podia ver um saldo que não vinha da mesma sessão. O catálogo mistura ações e minicontratos, e o resultado de um ponto de índice não é o de uma ação. A conta é uma só e será exposta na VPS sem autenticação, então a demonstração precisa poder recomeçar do zero.

## Decisão
1. **Uma sessão, um livro por ativo.** `PaperTradingSession` continua singleton, com um relógio simulado e um `PaperExecutionProvider` mais um `ExecutionCoordinator` por instrumento do catálogo. O feed publica um print de cada ativo no mesmo instante. O índice do caminho de preço e o instante do próximo print pertencem à sessão.
2. **Leituras derivadas.** `GET /api/terminal` devolve cotações, portfólio marcado, ordens, desempenho e trades fechados a partir dos prints e dos fills dessa sessão. Gráfico (`GET /api/chart`) e livro (`GET /api/market/depth`) continuam em endpoints próprios e usam os mesmos prints. Não há candle pintado nem escada de cinco níveis inventada.
3. **Valor do ponto fora da posição.** O ponto fica em `AssetMarketProfile` e entra só na leitura: risco pré-trade, P&L realizado, P&L não realizado e relatório. `Position` segue em preço negociado vezes quantidade. O caixa da demonstração permanece a constante `100000`. Ações valem 1; WING26 vale 0,20; WDOU26 vale 10.
4. **Reset da conta compartilhada.** `POST /api/session/reset` apaga ordens e fills persistidos e o estado em memória, e pede ao feed para reabrir o pregão em 2026-10-01 10:00:00 UTC. O recomeço é aplicado no mesmo lock da publicação, para o próximo print não voltar no tempo. A interface pede confirmação, porque a conta é a mesma para todos os visitantes.
5. **Polling permanece (ADR-041).** Falha de transporte limpa os saldos exibidos em vez de manter um número antigo.

## Consequências
- O visitante percorre um pregão coerente: cotação, gráfico, ordem no ativo selecionado, livro, zeragem, relatório e reinício.
- Continuam fora deste corte: robô, stop, OCO, dado real de mercado, autenticação e conta por usuário.
- Uma gravação que ainda estava em voo pode recriar uma ordem depois do apagamento, se a persistência terminar após o reset. A demonstração aceita essa corrida. Os testes esperam a submissão terminar antes de reiniciar.
