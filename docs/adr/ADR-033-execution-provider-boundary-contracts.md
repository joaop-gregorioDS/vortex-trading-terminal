# ADR-033 — Execution Provider Boundary Contracts

- Status: Accepted
- Date: 2026-10-05
- Scope: TASK 0.40

## Context

TASK 0.39 approved the minimum execution boundary after the Order Foundation checkpoint
5788c26c44c0fb018997480286eab85a0fea5870. Order protects its lifecycle and exact quantity invariants,
but must not know how it is executed. Passing the mutable entity to providers would distribute
lifecycle authority. Paper simulation, historical execution and future network adapters need one
consumer-owned port without requiring Account, Position, Risk or an entire OMS first.

## Decision

Add exactly six production types in VortexTrading.Application.Trading.Execution: IExecutionProvider,
OrderSubmission, ExecutionReport, ExecutionReportKind, ExecutionProviderFailureKind and
ExecutionProviderException. Preserve Application -> Domain and Infrastructure -> Application + Domain.
No existing files, dependencies or Domain types are changed.

OrderSubmission is a detached immutable class with an explicit canonical-value constructor:
OrderId, InstrumentId, Side, Type, Quantity, LimitPrice, TimeInForce and CreatedAt. Check required
components, defined enums, positive quantity and Market/Limit price shape only. Do not perform
contextual validation or expose an Order-taking factory. Market has null LimitPrice; Limit retains
a positive canonical Price. CreatedAt is intent creation; submission time is supplied separately.
OrderId is the only public Vortex submission identity.

IExecutionProvider exposes a stable trimmed ProviderKey, SubmitAsync, RequestCancelAsync and
IAsyncEnumerable<ExecutionReport> ReadReportsAsync, with explicit UtcInstant command times and
optional cancellation tokens. Normal SubmitAsync completion means initial operational response
and corresponding Accepted/Rejected fact availability, not Order acceptance or completion.
Normal cancellation completion means confirmed effective cancellation and Cancelled availability.
Intermediate acknowledgments do not establish cancellation. Only reports authorize future
Application transitions; command Tasks return no second lifecycle result.

ExecutionReport is a sealed immutable closed representation with private constructors and
FromAccepted, FromRejected, FromFill and FromCancelled factories. Its getter-only state is OrderId,
Kind, ObservedAt and optional Fill. Only Fill contains a payload, reusing the exact existing OrderFill
and deriving OrderId from it. No duplicate fill DTO, raw provider payload, rejection detail or
provider order identifier is introduced. ObservedAt remains independent of economic ExecutedAt,
Application receipt and Order application time.

Future producers own stable ExecutionId generation/normalization. External order/execution IDs stay
adapter-side. Future Application owns contextual validation, explicit transition times, workflow
and single-owner serial access to Order. One logical report consumer is started before commands
and continues while commands are pending. Providers normalize consistent per-Order lifecycle
ordering. Duplicates may occur; equivalent fill redelivery retains its identity and content, while
future Application suppresses repeated non-fill transitions. No sequence, deduplication, durability,
reconnection or exactly-once machinery is implemented.

Operational failure is distinct from business rejection. Failure kinds are Unavailable,
TransportFailure, Timeout, InvalidProviderResponse, AdapterFailure, CancellationRefused and
OutcomeUnknown. ExecutionProviderException validates classification/key/message and preserves
optional OrderId, producer-supplied OutcomeUncertain and sanitized text. Trim ProviderKey without
case conversion. Do not infer dispatch evidence from categories or concatenate raw diagnostics.
No inner-exception constructor is provided; SDK failures remain adapter-private.

Only a Rejected report represents confirmed provider business rejection. Technical failure never
automatically rejects Order; refusal/timeout/transport failure never establishes cancellation.
Canceling stream consumption does not cancel external Orders. Commands interrupted after possible
dispatch must preserve uncertainty; callers reconcile uncertain outcomes before retrying.

No provider implementation, orchestration, DI registration, matching or persistence is added.
No autonomous clock or identity generation is present. Application's future Paper simulation can
remain deterministic; external broker adapters belong in Infrastructure. Generic contracts do not
depend on Market Data, Replay, Indicator Engine, Chart, API or SDKs.

## Consequences

- Providers cannot receive or mutate Order through the contract.
- Canonical immutable values cross the boundary without rounding or primitive conversion.
- Four normalized facts have one lifecycle authority; operational errors cannot masquerade as them.
- Commands and streams require a future owner capable of consuming reports while commands await completion.
- Constructors validate shape but do not establish negotiability, account permissions or pre-trade approval.
- ProviderKey validation on an interface is an implementer obligation; the exception enforces its own key normalization.
- Caller-provided exception text must already be sanitized; no universal redaction claim is made.
- Live adapters require explicit recovery/reconciliation design for terminal conflicts, corrections and exactness constraints.
- Tests can establish immutable shapes and public signatures without implementing execution behavior.
- ADR status remains Proposed until a separate approved checkpoint.

## Alternatives

Direct provider mutation of Order reduces initial plumbing but confuses lifecycle ownership.
A single command result cannot express later fills and cancellation. Provider-specific DTOs leak
protocols into consumers. A message-driven execution subsystem adds persistence, schemas and delivery
machinery prematurely. Four report subclasses add public types without improving the closed factories
needed here. A duplicate fill DTO adds conversion and duplicate truth. Inner SDK exceptions would
expose external types and diagnostics through the normalized failure boundary.

## Deferred decisions

Concrete Paper/broker providers; contextual validation/orchestration; execution scope hosting;
capacity and recovery policies; Account/Position/Risk; instrument resolution; matching and causal
eligibility; slippage/liquidity; durable mapping and processed-fact storage; reconnection; corrections,
busts and terminal reconciliation; fees; additional Order types/TIF; API/UI; concurrency infrastructure.
The known EX-12 editorial inconsistency in ADR-032 is intentionally left untouched.

## References

- TASK 0.39 — Execution Provider Boundary Architecture Design, approved.
- ADR-032 — Minimal Order Domain Foundation, Accepted.
- docs/architecture/execution-provider-boundary-contracts.md.
