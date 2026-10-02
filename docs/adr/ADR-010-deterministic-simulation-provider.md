# ADR-010 — Deterministic Simulation Provider

- Status: Accepted
- Date: 2026-10-02.
- Scope: Primeiro adapter concreto de Market Data, offline e determinístico, sem registro em DI.

## Context

Domain possui Quote, Trade e Candle canônicos, e Application possui IMarketDataProvider. Antes de introduzir protocolos de rede e peculiaridades de fonte, precisamos comprovar assinaturas, capacidades, enumeração assíncrona e cancelamento contra essa porta.

Dados dependentes de relógio real ou aleatoriedade tornariam os testes menos reproduzíveis. A primeira implementação deverá ter entradas explícitas, sequência finita e progressão independente por consumo.

## Decision

The first concrete market-data adapter is an offline, finite and deterministic SimulationMarketDataProvider.

It validates the Application provider boundary without introducing network protocols, credentials, external SDKs, real-time timing or provider-specific complexity.

Implementar o adapter exclusivamente em Infrastructure, com descriptor simulation / Simulation e as capacidades Quotes | Trades | Candles. Não alterar Domain, contratos Application ou composição de DI.

Usar SimulationMarketDataOptions imutável com StartTime, Price positivo, Quantity positiva e EventCount positivo por instrumento. Preservar preços/quantidades constantes; quotes locked, trades positivos e candles flat satisfazem as invariantes canônicas sem acrescentar aritmética financeira.

Quote/Trade avançam um segundo por rodada desde StartTime, com ReceivedTime = EventTime. Candles avançam pelo CandleInterval solicitado, preservando CloseTime derivado. Toda série deve caber nos limites temporais: options valida Quote/Trade; a assinatura valida o último fechamento de Candle com comparação por divisão antes de emitir dados. Extremos exatamente representáveis são permitidos.

Validar assinaturas na chamada, copiar os IDs, eliminar repetidos e ordenar pelo valor Guid. Rodadas percorrem os IDs nessa ordem. Não alterar a coleção original nem tratar IDs repetidos como solicitação de dados multiplicados.

Expor os streams canônicos tipados usando iterator assíncrono privado para compartilhar somente o percurso. Usar EnumeratorCancellation para honrar tokens da operação e da enumeração, com OperationCanceledException padrão e sem espera artificial. Cada consumo tem progresso local, finito e reiniciável.

## Consequences

- A inversão de dependência fica executável e testável sem acesso a rede, banco, Redis ou credenciais.
- Entradas/configuração iguais produzem sequências iguais; chamadas e enumerações intercaladas não compartilham contador.
- Guardas de assinatura, cancelamento e semântica de IDs repetidos do contrato Application têm implementação concreta.
- Price/Quantity são preservados em bits e escala, sem quantização ou caminho de overflow decimal na geração.
- Erros de configuração/intervalo temporal são detectados antes da série emitir dados, incluindo durações que excederiam long em multiplicações ingênuas.
- Nenhum provider concreto é registrado na composição principal antes de revisão arquitetural.
- O novo projeto Infrastructure.Tests usa apenas a referência de produção necessária e os pacotes de teste estabelecidos; sua inclusão na solution permite regressão conjunta sem ciclos.

## Alternatives

Provider real introduziria rede, credenciais, SDK e falhas externas antes de validar a porta. Random, mesmo com seed, acrescentaria geração desnecessária e casos menos transparentes. Clock real/Task.Delay confundiria timestamps de dados com agendamento de execução e exigiria testes temporais.

Progressões financeiras variáveis exigiriam política para overflow e redução de precisão decimal sem trazer benefício necessário para testar a fronteira. Constantes positivas preservam extremos e escala com uma configuração menor. Task<List<T>>, Channels ou buffering próprio alterariam ou complicariam a semântica de consumo aprovada. Contador global quebraria repetição e independência entre chamadas.

## Limitations

Os dados não simulam mercado realista: os mesmos valores financeiros atendem todos os instrumentos, quotes têm spread zero e candles são flat, sem agregação. EventCount é por instrumento; o total depende dos IDs distintos. Não há pacing real-time, latency configurável, calendário de sessão, alinhamento de candles ou consulta de metadata de catálogo.

Options deve comportar a progressão de Quote/Trade para qualquer provider configurado, mesmo em consumo apenas de candles. Ordering determinístico desta simulação não redefine garantias universais de Application ou feed real. A implementação não escolhe regras de deduplicação, sequência ou finalização de dados externos.

## Deferred Decisions

Adiar registro em DI, consumers/orquestração, normalização externa, envelopes/sequence, deduplicação de feed, realismo e variação financeira, candle aggregation, order book, aggressor side, retry/reconnect, Replay e seus clocks, providers reais, persistência, transporte, API e frontend.

## References

- ADR-006 — Temporal Semantics.
- ADR-007 — Market Data Core Model.
- ADR-008 — Trade Identity and Semantics.
- ADR-009 — Market Data Provider Boundary.
- Simulation Market Data Provider — TASK 0.12.
