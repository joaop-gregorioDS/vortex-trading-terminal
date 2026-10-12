# ADR-043 — Pre-Trade Risk Evaluation

- Status: Accepted
- Date: 2026-10-06
- Scope: TASK 9.0 (Desafio 1). Módulo de risco pré-trade e rejeição controlada de ordens.

## Context

O roadmap do projeto na Fase 4 estipulava a introdução do módulo de **Risk**, responsável pela validação e aprovação de ordens antes de seu envio à execução (`IExecutionProvider`), conforme estabelecido nos princípios arquiteturais de [ADR-001](ADR-001-modular-monolith.md) e [ADR-032](ADR-032-minimal-order-domain-foundation.md).

No estágio anterior, qualquer ordem válida do ponto de vista sintático era enviada diretamente ao `OrderExecutionWorkflow` e à `PaperExecutionProvider`, independentemente de volume excessivo (*fat-finger*), financeiro desproporcional ou acúmulo descontrolado de posição. Além disso, o domínio já previa explicitamente o enum `OrderRejectionReason.PreTradeValidationFailed`, porém não havia um motor de regras que o disparasse.

## Decision

**Criação do módulo independente `VortexTrading.Application.Risk`.**
O módulo reside na camada de Application e provê:
1. `RiskDecision`: Representação imutável do veredito (`Approve` ou `Reject` com motivo textual explícito).
2. `RiskEvaluationContext`: Dados contextuais passados para avaliação: a ordem candidata (`Order`), o estado da carteira (`Portfolio`), o preço de referência atual (`Price?`) e ordens ativas/pendentes no livro.
3. `IRiskRule` e regras pré-trade:
   - `MaxOrderQuantityRule`: Impede ordens com quantidade superior ao teto configurado.
   - `MaxOrderNotionalRule`: Avalia o valor financeiro nocional estimado (\(\text{Quantidade} \times \text{Preço}\)) e rejeita ordens que excedam o limite financeiro.
   - `MaxPositionQuantityRule`: Projeta a nova exposição da posição (posição atual + ordens de compra ativas pendentes + nova ordem) e rejeita ordens que excederiam a quantidade máxima de inventário.
   - `ReferencePriceForMarketOrdersRule`: Rejeita ordens a mercado quando o mercado não dispõe de preço de referência recente.
4. `RiskEvaluator`: Avaliador composto com suporte a curto-circuito na primeira falha e factory a partir de `RiskOptions`.
5. `RiskOptions`: Parâmetros configuráveis via `IConfiguration` na seção `VortexTrading:Risk`.

**Acolhimento da rejeição no escopo de execução.**
O `ExecutionCoordinator` recebeu o método `RecordPreTradeRejectedOrderAsync`, permitindo registrar e persistir (`IOrderStore`) uma ordem que foi rejeitada em pré-trade sem acionar o `OrderExecutionWorkflow` nem poluir o livro da `PaperExecutionProvider`.

**Ordem rejeitada possui ciclo de vida preservado e visibilidade operacional.**
Quando uma ordem é rejeitada pelo risco:
- Seu status transita para `OrderStatus.Rejected` com `OrderRejectionReason.PreTradeValidationFailed`.
- É salva no PostgreSQL e consultável em `GET /api/orders` e `GET /api/status` (`rejectedOrderCount`).
- O frontend apresenta a ordem como `Rejeitada` em tom visual apropriado, mantendo a integridade da mesa de operação.

## Consequences

- Ordens com quantidades ou valores fora dos parâmetros de risco são contidas antes de chegar à exchange.
- O histórico de ordens rejeitadas permanece auditável e persistido no banco de dados.
- Não há acoplamento de tipos de risco à fronteira de execução (`Trading.Execution`), preservando as guardas arquiteturais existentes.
- A suíte de testes de backend aumentou de 2.133 para 2.157 testes, todos aprovados com 100% de sucesso.

## References

- ADR-001 — Modular Monolith.
- ADR-032 — Minimal Order Domain Foundation.
- ADR-037 — Execution Scope Coordination.
- ADR-042 — Operational Visibility.
