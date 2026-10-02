# Market Data Boundary

## Posição e direção de dependência

Application possui IMarketDataProvider porque seus consumidores definem a porta necessária para obter observações canônicas. Infrastructure implementará adapters específicos de fonte contra essa porta. Domain permanece sem conhecimento de providers.

As referências continuam apontando para dentro: Application referencia Domain; Infrastructure referencia Application/Domain; API compõe essas camadas. A interface e seus tipos auxiliares não referenciam Infrastructure, API ou transporte. O FrameworkReference já existente de Application permanece inalterado; os novos contratos utilizam somente BCL e Domain.

## Contrato e streaming

IMarketDataProvider expõe um MarketDataProviderDescriptor estável e três operações:

| Operação | Entrada | Saída |
| --- | --- | --- |
| StreamQuotesAsync | instrumentIds, cancellationToken | IAsyncEnumerable<Quote> |
| StreamTradesAsync | instrumentIds, cancellationToken | IAsyncEnumerable<Trade> |
| StreamCandlesAsync | instrumentIds, interval, cancellationToken | IAsyncEnumerable<Candle> |

Não há stream de object ou MarketEvent. A enumeração assíncrona permite consumir observações conforme ficam disponíveis. O contrato não escolhe transporte, buffering, concorrência, batching ou limites de símbolos. Não há implementação concreta ou registro de provider em DI nesta tarefa.

Todas as operações exigem CancellationToken explícito, sem timeout interno. Implementações futuras deverão respeitar cancelamento durante consumo e encerrar/liberar a assinatura e seus recursos. OperationCanceledException mantém a semântica padrão do .NET; não representa falha de conexão nem dispara reconexão automática. A implementação deverá integrar também o token de cancelamento recebido pela enumeração assíncrona.

## Assinaturas e validação

instrumentIds usa IReadOnlyCollection<InstrumentId> não nullable, sem carregar Instrument, Symbol ou Venue. A interface declara o contrato; a implementação futura será responsável pela validação em runtime:

- Coleção null: ArgumentNullException.
- Coleção vazia ou contendo ID null: ArgumentException. Vazio nunca significa todos os instrumentos.
- Vários IDs: uma assinatura lógica para os instrumentos solicitados, sem limite imposto nesta fundação.
- IDs repetidos: não alteram o significado da assinatura nem solicitam entrega multiplicada. Isso não define deduplicação de eventos do feed. Não há mecanismo de normalização/deduplicação de assinaturas implementado agora.
- Candles: CandleInterval obrigatório, único para os instrumentos daquela operação; null gera ArgumentNullException. A validade da duração já pertence à primitive. Suporte a intervalos/instrumentos concretos dependerá do provider.
- Capacidade ausente no descriptor: NotSupportedException antes de qualquer observação emitida.

Validação pode ocorrer na chamada ou no início da enumeração, devido à execução adiada de iterators assíncronos, mas deve anteceder o primeiro dado. A anotação nullable impede uso null sem diagnóstico de compilador; a interface isolada não executa guards.

IReadOnlyCollection não garante imutabilidade da coleção original. O chamador deve mantê-la inalterada durante o consumo; uma implementação poderá capturar uma cópia para estabilizar sua assinatura. Não há request type adicional nesta etapa.

## Identidade e descriptor

MarketDataProviderDescriptor é um sealed record imutável com apenas Key, DisplayName e Capabilities. Key é identidade lógica textual normalizada por Trim e ToLowerInvariant; DisplayName recebe Trim preservando caixa. Ambos rejeitam null/blank com ArgumentException. Não há enum fechado de providers, Guid aleatório ou primitive ProviderKey separada: uma string validada dentro do descriptor atende à necessidade atual com menos tipos.

Key distingue providers no contexto da aplicação; sua unicidade no futuro registro/orquestração não é imposta por um valor isolado. O descriptor usa igualdade/hash estruturais dos três componentes; mudar nome ou capacidades muda o valor do descriptor, sem redefinir a chave lógica.

Capabilities deve conter pelo menos uma capacidade conhecida, sem bits indefinidos; valores inválidos geram ArgumentOutOfRangeException. O descriptor não contém credenciais, API keys, secrets, URLs, connection strings, rate limits, runtime health ou latência.

## Capacidades

MarketDataCapabilities é flags enum: None = 0, Quotes = 1, Trades = 2, Candles = 4. As sete combinações não vazias são válidas no descriptor; None é o valor vazio do enum, mas não descreve um provider utilizável nesta fronteira.

As capacidades são acessadas por provider.Descriptor.Capabilities, sem propriedade duplicada. Descrevem somente quais formatos canônicos o provider pode fornecer. Não significam real-time, historical, certificação, baixa latência, completude ou suporte a todo instrumento/intervalo. Nenhuma capacidade de order book, Replay, notícias ou fundamentals é introduzida.

## Ordering e delivery

O contrato não garante exactly once, at least once, global ordering, cross-instrument ordering, deduplicação ou gap-free delivery. Ordem de yield não implica ordem por EventTime; ordem por ReceivedTime não implica ordem por EventTime. EventTime e ReceivedTime mantêm as semânticas temporais aprovadas, sem substituição ou ordenação forçada.

Igualdade de Quote/Trade/Candle não é uma regra de deduplicação da fronteira. Garantias de entrega, detecção de gaps e ordenação precisarão de contratos concretos do provider e da futura normalização.

## Failure e reconnection

Implementações futuras precisarão distinguir falhas de configuração, autenticação, conexão, assinatura e protocolo, preservando cancelamento como categoria distinta. Nenhuma hierarquia própria de exceptions é criada agora. Falhas não devem ser silenciosamente convertidas em stream vazio ou sucesso; uma falha pode aparecer na abertura ou durante enumeração.

Reconexão, retry e backoff pertencem à futura implementação/orquestração, sem promessa de reconexão infinita, retry forever ou exactly-once delivery. Finalização normal de um stream não comprova completude ou ausência de gaps.

## Metadata e normalização futura

```text
Provider-specific payload
        ↓
Infrastructure adapter
        ↓
Normalization / validation
        ↓
Canonical Domain Model
        ↓
Application consumers
```

Provider/exchange IDs, sequence, raw payload, timestamps específicos da fonte e metadata de transporte serão preservados, quando necessário, em camada/envelope de ingestão próprio. Quote, Trade e Candle permanecem inalterados e não absorvem esses campos.

A localização detalhada, interfaces e comportamento da normalização ainda serão decididos; a direção acima não implementa pipeline nem transfere parsing específico de fonte ao Domain. Validação com catálogo/increments exigirá contexto externo ao snapshot.

## Validação e decisões adiadas

O projeto Application.Tests testa invariantes do descriptor: campos obrigatórios, normalização, flags conhecidas/combinações, igualdade/hash e cultura. Não há testes artificiais por reflection da interface, mocking library ou fake provider. A solution inclui esse projeto para executar seus testes junto dos 274 testes de Domain existentes.

Ficam adiados providers de simulação, replay e mercado real; contratos concretos de falha/metadata; request types se necessários; registry/unicidade de chaves; batching/limites; normalização; deduplicação/sequence; reconexão/backoff; clocks; persistência; transporte e APIs.

A decisão está registrada no ADR-009 — Market Data Provider Boundary, com status Proposed para revisão.
