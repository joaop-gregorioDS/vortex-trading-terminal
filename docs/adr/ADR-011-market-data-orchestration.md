# ADR-011 — Market Data Orchestration

- Status: Accepted
- Date: 2026-10-02.
- Scope: Orquestração inicial de streams canônicos em Application, sem composição de DI.

## Context

Domain possui Quote, Trade e Candle canônicos, Application define IMarketDataProvider e Infrastructure possui o primeiro adapter determinístico. Antes de adicionar Replay ou integrações externas, consumers precisam de uma camada de Application que coordene assinaturas sem conhecer implementações concretas.

O contrato existente permite validação na chamada ou no início da enumeração e documenta coleções válidas, capabilities e cancelamento. A orquestração torna a validação da requisição consistente antes da delegação e protege subscriptions contra alterações posteriores em coleções mutáveis do chamador.

## Decision

Application owns a provider-agnostic MarketDataOrchestrator.

The orchestrator validates and normalizes subscription requests, validates provider capabilities, and transparently forwards canonical asynchronous market-data streams.

It does not perform event normalization, persistence, caching, transport, provider selection or background processing.

Receber exatamente um IMarketDataProvider no construtor, rejeitar null e delegar ProviderDescriptor ao descriptor original. Manter o componente em MarketData/Orchestration sem interface adicional e sem registro em DI nesta etapa.

## Responsibilities

- Rejeitar assinaturas null, vazias ou com IDs null na chamada, e CandleInterval null para candles.
- Copiar a assinatura, deduplicar InstrumentId por valor e ordenar por InstrumentId.Value com o comparador padrão de Guid.
- Entregar o snapshot normalizado em coleção somente de leitura, independente da coleção original.
- Exigir a flag correspondente a Quotes, Trades ou Candles; ausência causa NotSupportedException antes de invocar a operação do provider.
- Delegar operações tipadas, intervalo e token da operação sem alteração e retornar o mesmo IAsyncEnumerable do provider.
- Preservar instâncias dos eventos, ordem, quantidade, valores e timestamps, mantendo consumo lazy dirigido pelo chamador.
- Propagar cancelamento e falhas originais sem captura ou wrapping; deixar enumeração e descarte sob controle do consumer.

## Consequences

- Consumers podem depender de Application sem depender de adapters concretos de Infrastructure.
- Assinaturas semanticamente equivalentes chegam ao provider na mesma ordem, sem alterar ou depender de alterações posteriores na coleção do chamador.
- Validações da requisição são síncronas; a operação do provider é chamada após as guardas, enquanto os eventos só são enumerados pelo consumer.
- O snapshot usa memória proporcional ao número de IDs, mas a camada não armazena ou materializa eventos.
- Não há estado compartilhado de progresso. Garantias de repetição, ordering dos eventos e concorrência continuam pertencendo ao provider.
- Tokens da operação e da enumeração mantêm a semântica da implementação existente, pois não há wrapper de iterator.
- Application.Tests usa um test double próprio, sem Infrastructure, mocking framework, novo projeto ou pacote.
- Domain, contratos da porta, Simulation provider, persistence, API, DI e frontend permanecem inalterados.

## Alternatives

Consumers chamarem adapters concretos violaria a direção de dependências. Consumir a porta diretamente em todos os pontos dispersaria validação e normalização de assinaturas.

Criar uma interface para um único orchestrator sem segunda implementação acrescentaria abstração sem necessidade nesta etapa. Um async iterator de forwarding repetiria a lógica de cancelamento/descarte sem transformar eventos; retornar o stream original preserva essas regras diretamente.

Event bus, background services, buffers, cache ou seleção/fallback de providers ampliariam o escopo antes de validar a cadeia mínima de consumo. Alterar o contrato do provider não é necessário para esta responsabilidade.

## Deferred Decisions

Adiar registro/lifetime em DI, seleção e registry de providers, fallback/failover, políticas de retry/reconnect, Historical Replay e seus clocks, providers reais, envelopes, normalização de eventos, sequences, feed deduplication, gap detection, cache, agregação de candles, persistência, Redis, transporte, SignalR, API, frontend e processamento em background.

## References

- ADR-009 — Market Data Provider Boundary.
- ADR-010 — Deterministic Simulation Provider.
- Market Data Orchestration — TASK 0.13.
