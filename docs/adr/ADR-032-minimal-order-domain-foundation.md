# ADR-032 — Minimal Order Domain Foundation

- Status: Accepted
- Date: 2026-10-05
- Scope: TASK 0.38

## Context

TASK 0.37 approved the minimum Order language and lifecycle before introducing Paper Trading. Market/temporal primitives and Market Data, replay, indicators and charts are frozen. Trading currently provides financial primitives, but no Order, Account, Position or execution provider.

The first Order must own intrinsic invariants without becoming a broker DTO, adding matching or requiring event sourcing. Redelivered fills require identity. OD-08 demonstrates that checked decimal addition alone cannot guarantee conservation: a tiny positive quantity can disappear without overflow. Remaining quantity subtraction can suffer the same problem.

## Decision

Add exactly nine production types in VortexTrading.Domain.Trading: OrderId, ExecutionId, OrderSide, OrderType, TimeInForce, OrderStatus, OrderRejectionReason, OrderFill and Order.

Order is a sealed guarded entity, with equality/hash exclusively by immutable OrderId. Both identity VOs are caller-supplied non-empty GUIDs with value equality and no random generation. Instrument reference is InstrumentId only. Immutable intention is constructed through CreateMarket/CreateLimit factories; the constructor is private.

Support Buy/Sell, Market/Limit and GTC only. Market carries no price; Limit requires a positive LimitPrice. Requested quantity is positive. Existing primitives are unchanged. All public enum inputs are validated; unsupported enum members are not introduced.

Lifecycle is Created, Accepted, PartiallyFilled, Filled, Cancelled and Rejected. Construction is valid local intention, not contextual validation or executor acceptance. Accept is Created-only. Created can reject with PreTradeValidationFailed or ExecutionProviderRejected. Accepted can reject only without fills and with ExecutionProviderRejected. Created, Accepted and PartiallyFilled can cancel. Only Accepted/PartiallyFilled can receive new fills. Filled/Cancelled/Rejected are terminal.

OrderFill is an immutable value record containing ExecutionId, OrderId, positive Quantity/Price and explicit ExecutedAt. Order owns applied fills through a privately mutated list and read-only view in application order. FilledQuantity derives from the exact sum of these facts; RemainingQuantity derives from requested-minus-filled and may stay positive after cancellation. Neither is an independent mutable truth. Limit Buy fills cannot exceed its limit; Limit Sell fills cannot fall below it.

ApplyFill returns true for a new accepted fill and false for equivalent redelivery. Known IDs are checked before terminal/time monotonicity guards. Equivalent content includes both IDs, numeric quantity/price and normalized ExecutedAt; decimal scale and original UTC offset do not manufacture differences. Conflicting redelivery throws DomainException with zero mutation, including in terminal states. Different IDs with otherwise identical economics remain distinct fills. All calls require a non-null fill and application time; an equivalent duplicate does not require its application time to be monotonic because no transition occurs.

Privately inspect decimal coefficient/scale and represent quantities as BigInteger units of 10^-28. Compute the candidate sum and remainder exactly; reject overfill and reject either result if it cannot fit a decimal without discarding any significant digit. Only exact trailing coefficient zeroes can be removed during conversion. DomainException is raised before mutation; no double, float, rounding repair or public mathematical abstraction is added. Original input primitive representation is preserved; derived values may have normalized trailing zeroes.

Every effective operation receives an explicit UtcInstant at >= LastUpdatedAt; equal instants are allowed. No clock is consulted. ExecutedAt describes occurrence independently of application order. CreatedAt remains immutable. CompletedAt derives from terminal LastUpdatedAt and is absent while active. Equivalent redelivery never changes timestamps.

Required validation and candidate calculations precede publication. Invalid transitions, null inputs, bad times, conflicting/wrong-order fills, limit violations, overfill and unrepresentable arithmetic are atomic DomainException failures. No formal domain events, event store, version or sequence is introduced. Application will own serial access and future concurrency/persistence policy.

Contextual existence, increments, account permissions, balance, positions, hours, capabilities and authorization of cancellation/acceptance remain outside Order. Domain cancellation represents an effective decision, not a request in flight. No Application/Infrastructure/API/frontend production code changes are needed.

## Consequences

- Consumers share one intrinsic lifecycle and cannot set status or mutate fill history externally.
- Stable execution identity prevents double application without mistaking market Trade equality for execution identity.
- Quantity conservation includes both cumulative and remaining quantities; some individually representable fills are rejected when their exact aggregate/remainder is not representable.
- Deriving quantities is linear in applied fill count, avoiding a second source of truth and premature caching.
- Explicit IDs/times and deterministic guarded transitions support future Paper Trading/Backtesting without coupling Order to replay.
- Terminal states are protected; late fills/corrections require an explicit future reconciliation design, not silent reopening.
- Account scope and asynchronous provider workflows remain prerequisites for integration, rather than fake fields in this Domain foundation.
- ADR status remains Proposed until the separate architectural checkpoint decision.

## Alternatives

An anemic immutable record would distribute lifecycle protection across consumers and permit inconsistent snapshots. An event-sourced aggregate would require event schema, replay/versioning and persistence decisions without a current need. Aggregate quantity alone would not protect against duplicate fills or conflicting identity payloads.

Checked decimal sums detect overflow but do not detect silent absorption. Arithmetic round-trip heuristics would require additional proof and can themselves round. Integer coefficient/scale inspection directly establishes exact representability. A public decimal mathematics service, rounding to instrument increments or changing Quantity would unnecessarily affect frozen foundations.

## Deferred Decisions

ClientOrderId/submission idempotency, Account/Portfolio, Position, Risk, full Execution model, execution provider interface/adapters, Paper Trading/matching, broker IDs/payloads, persistence/concurrency versions, fees/average execution price, Stop/StopLimit, Day/IOC/FOK, replacement, corrections/busts, asynchronous reconciliation and API/UI are deferred. OCO is a relationship/orchestration of independent Orders and is not an OrderType.

## References

- TASK 0.37 — Trading Order Domain Architecture Design, approved.
- ADR-004 — Financial Domain Primitives.
- ADR-005 — Market Instrument Identity.
- ADR-006 — Temporal Semantics.
- ADR-008 — Trade Identity and Semantics.
- docs/architecture/minimal-order-domain-foundation.md.
