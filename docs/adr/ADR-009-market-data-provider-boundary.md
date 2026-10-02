# ADR-009 — Market Data Provider Boundary

- Status: Accepted
- Date: 2026-10-02.
- Scope: Contratos de Market Data em Application, sem providers concretos ou pipeline.

## Context

Quote, Trade e Candle estão estabilizados como modelos canônicos explícitos. A aplicação precisa consumi-los de diferentes fontes sem depender de SDK, transporte ou convenções de provider. Colocar essa abstração no Domain misturaria o vocabulário do mercado com a integração externa.

Uma porta mínima deve identificar providers, anunciar os formatos disponíveis e expressar assinaturas tipadas com cancelamento, sem prometer garantias que ainda dependem de contratos concretos de fonte.

## Decision

Application owns the market-data provider abstraction.

Infrastructure will implement provider-specific adapters.

Domain remains unaware of providers.

Canonical Quote, Trade and Candle models cross the Application boundary without provider-specific metadata.

Criar IMarketDataProvider em Application com Descriptor e StreamQuotesAsync, StreamTradesAsync e StreamCandlesAsync. Retornar IAsyncEnumerable do tipo canônico correspondente, sem stream de object, base MarketEvent, Rx, Channels ou dependências externas novas. Todas as operações recebem CancellationToken obrigatório.

Usar IReadOnlyCollection<InstrumentId> não nullable para múltiplos instrumentos; candles recebem também CandleInterval. Implementações futuras deverão rejeitar coleção null/vazia, IDs null e intervalo null antes do primeiro dado. IDs repetidos não alteram o significado da assinatura. Essas regras são contratuais, sem criar request type ou implementação nesta tarefa.

Usar MarketDataProviderDescriptor como sealed record imutável com Key textual normalizada por Trim/ToLowerInvariant, DisplayName aparado e Capabilities. Campos textuais null/blank são inválidos. Não criar ProviderKey separado, identidade aleatória ou lista fechada de providers.

MarketDataCapabilities é flags enum com None, Quotes, Trades e Candles. Descriptor exige pelo menos uma capacidade conhecida e rejeita bits indefinidos. As capacidades indicam somente formatos canônicos disponíveis, sem garantir real-time/historical, completude ou suporte a qualquer instrumento/intervalo. Capacidade ausente deve falhar com NotSupportedException antes de emitir dados.

Não garantir ordering, exactly once, at least once, deduplicação ou ausência de gaps. Não definir hierarquia própria de falhas, retry ou reconexão. OperationCanceledException preserva a semântica padrão do .NET. Identidade/sequence e outros dados específicos da fonte deverão permanecer na futura camada/envelope de ingestão.

## Dependency Inversion

Consumidores em Application dependem da porta que a própria camada define. Infrastructure dependerá de Application para implementar a porta, e API fará composição. Application não referencia Infrastructure; Domain não referencia Application nem providers. Não há alteração de referências dos projetos de produção ou de DI.

O novo projeto Application.Tests referencia apenas Application, usando os mesmos pacotes/versões de teste estabelecidos; Domain é acessível transitivamente e não exige referência direta para os testes do descriptor. Sua inclusão na solution permite validação conjunta, sem ciclo.

## Consequences

- Contratos tipados mantêm diferenças semânticas entre Quote, Trade e Candle.
- Nullable annotations e documentação estabelecem pedidos válidos; implementações futuras deverão executar a validação, possivelmente no início da enumeração adiada.
- Não há inscrição implícita em todos os instrumentos; multiplicidade de IDs não define multiplicidade de entrega ou deduplicação de feed.
- Descriptor pequeno permite identidade e seleção por capacidades sem armazenar configuração/credenciais, health ou latência.
- Nenhuma infraestrutura de consumo, provider concreto, cliente de transporte ou loop de reconexão é introduzido.
- Ordering, suporte a instrumentos/intervalos, falhas e delivery continuam dependendo do provider e das futuras decisões de normalização.
- IReadOnlyCollection não congela o objeto original; o chamador mantém a coleção estável durante consumo, e adapters poderão capturar cópia.
- Domain, Infrastructure, API, frontend e persistência permanecem inalterados.

## Alternatives

Colocar IMarketDataProvider em Domain criaria conhecimento de integração na camada canônica. Defini-lo em Infrastructure obrigaria consumidores a dependerem da implementação externa. Um stream genérico ou MarketEvent base apagaria diferenças de modelos para compartilhar poucos campos.

Operações somente por instrumento limitariam a expressão de assinaturas sem necessidade. Um request gigante, ProviderKey separado, registry ou envelope completo anteciparia políticas sem contratos reais. Rx/Channels acrescentariam mecanismos de consumo nesta fase. Um enum fechado de providers ou Guid aleatório não representaria uma identidade lógica extensível.

## Deferred Decisions

Adiar providers de simulação/replay/mercado real, SDKs/transporte, registry de providers, unicidade de chaves, request objects e normalização de IDs, batching/limites, instrument/interval support, catálogo/increments, contratos detalhados de falha, metadata/envelope, sequence, ordering/delivery/deduplicação/gaps, reconexão/backoff, clocks, agregação, persistência e APIs.

## References

- ADR-001 — Modular Monolith.
- ADR-005 — Market Instrument Identity.
- ADR-006 — Temporal Semantics.
- ADR-007 — Market Data Core Model.
- ADR-008 — Trade Identity and Semantics.
- Market Data Boundary — TASK 0.11.
