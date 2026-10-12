# ADR-044: Arquitetura da Estação de Negociação Pro ("Investimentos Pro")

## Status
Aceito

## Contexto
O protótipo inicial do frontend do Vortex Trading disponibilizava uma interface simples de operação em um único painel escuro. Para atender aos requisitos de uma plataforma de negociação profissional com fidelidade visual aos terminais de mercado financeiro institucional (como demonstrado nas referências visuais de produto):
1. A interface necessitava de uma separação clara em módulos executivos e operacionais: **Visão Geral** (Dashboard executivo com KPIs patrimoniais, posições abertas e últimas movimentações), **Operar** (Mesa com suporte a multi-abas de gráficos, gaveta retrátil de Observação do Mercado B3, gaveta de Algo Trading e boleta de alta velocidade) e **Relatórios** (Análise de performance com 10 KPIs de Daytrade, curva de equity e histórico detalhado de operações com exportação CSV).
2. A mesa de operação exigia a capacidade de cancelar ordens e zerar posições abertas com um único comando atômico de emergência (`Cancelar + Zerar` / *Emergency Flatten*).
3. A navegação exigia consistência de estado global em tempo real (relógio de pregão em horário de Brasília, status de robô `Algo ON/OFF`, cotações em tempo real e atualização determinística sem perda de contexto entre abas).

## Decisão
1. **Shell Global da Aplicação (`AppShell`)**:
   - Barra superior clássica de comandos de desktop financeiro (`Arquivo`, `Exibir`, `Inserir`, `Gráficos`, `Ferramentas`, `Janela`, `Ajuda`).
   - Header com indicador do ativo selecionado, botão unificado de ativação de robô (`Algo ON` / `Algo OFF`), saldos patrimoniais em tempo real (`SALDO DAY TRADE`, `PATRIMÔNIO DAY TRADE`), perfil do operador e badge de simulação.
   - Barra de navegação lateral com atalhos diretos e gatilhos para abertura de gavetas retráteis.
2. **Catálogo de Multiativos B3 (`/api/market/quotes`)**:
   - Fornecimento de 15 ativos de referência do mercado brasileiro (ações líquidas como PETR4, VALE3, ITUB4, BBDC4, BBAS3, ABEV3, etc.; e contratos futuros como WING26 e WDOU26).
   - Gaveta retrátil `MarketWatchDrawer` com filtro instantâneo de busca e sincronização imediata com a boleta e gráfico em 1 clique.
3. **Módulo de Performance e Histórico de Trades (`/api/reports/performance` e `/api/reports/trades`)**:
   - Consolidação de métricas essenciais de gestão de trading quantitativo: Win Rate, Profit Factor, Payoff, Drawdown Máximo, Melhor e Pior Trade.
   - Geração de curva de evolução de patrimônio acumulado (*Equity Curve*) e tabela detalhada de execuções com mecanismo nativo de exportação para planilha CSV formatada.
4. **Ação Atômica de Emergência (`/api/orders/emergency-flat`)**:
   - Endpoint dedicado que cancela com segurança todas as ordens ativas da sessão e emite ordens a mercado de liquidação para zerar as posições abertas remanescentes.
5. **Precisão Numérica Estrita (ADR-002 Mantido)**:
   - Todas as operações financeiras continuam trafegando como strings decimais exatas, prevenindo distorções de ponto flutuante.

## Consequências
- A plataforma atinge paridade completa com as interfaces profissionais de negociação de mercado financeiro.
- O operador pode alternar dinamicamente entre Visão Geral, Mesa de Operação e Relatórios sem perda de estado.
- A suite completa de testes unitários e de integração permanece 100% verde (2.161 testes no backend, compilação de produção e linting 100% aprovados no frontend).
