# Execution Provider Boundary Contracts

- Task: 0.40
- Date: 2026-10-05
- Architectural decision: ADR-033, Proposed pending review and checkpoint.

## Scope and responsibility

Six production types live in `VortexTrading.Application.Trading.Execution`:
`IExecutionProvider`, `OrderSubmission`, `ExecutionReport`, `ExecutionReportKind`,
`ExecutionProviderFailureKind` and `ExecutionProviderException`. Application already references
Domain; no dependency, package, registration or existing foundation changes are required.

| Owner | Responsibility |
|---|---|
| Domain Order | Intrinsic lifecycle, fill identity, limits and exact quantity conservation |
| Future Application orchestration | Contextual validation, commands, workflow, explicit application time and serial Order transitions |
| Provider contract | Immutable input, normalized facts and operational failure semantics |
| Future Paper implementation | Deterministic simulation and stable fill identity |
| Future broker adapter | External protocol, identity mapping, normalization and private diagnostics |

No provider or orchestrator is implemented here. The provider never receives the mutable Order.
Domain remains independent of Application and Infrastructure. Infrastructure can implement this
Application-owned port without reversing the existing dependency direction.

## Immutable submission

`OrderSubmission` is a sealed class with an explicit constructor and eight getter-only properties:
`OrderId`, `InstrumentId`, `Side`, `Type`, `Quantity`, `LimitPrice`, `TimeInForce`, `CreatedAt`.
Each uses the existing canonical Domain type. Immutable value objects are retained exactly;
there is no Order reference, collection, lifecycle state or provider-specific payload.

Required IDs, quantity and creation time cannot be null. Side, Type and TimeInForce must be defined.
Quantity must be positive; Market must have no LimitPrice; Limit must have a positive LimitPrice.
These are structural intent-shape checks at an independently constructible boundary, not a second
Order lifecycle or pre-trade engine. Instrument lookup, increments, risk, account permissions and
market hours are not validated. No rounding or primitive conversion occurs.

OrderId is the sole Vortex correlation identity. There is no submission, client-order or external-order
identifier. CreatedAt is the original intent creation instant; submittedAt is supplied separately.
The explicit constructor allows a caller to copy canonical intent values without passing an entity
to the provider. No factory accepting Order is exposed.

## Commands and initial completion

The port exposes a stable, non-null, non-blank, trimmed `ProviderKey`, two Task-returning commands
and one report stream. Trimming does not imply case conversion; producers must keep the key stable
within their execution scope. There is no descriptor, capability registry or vendor enum.

```text
SubmitAsync(OrderSubmission, UtcInstant submittedAt, CancellationToken = default)
RequestCancelAsync(OrderId, UtcInstant requestedAt, CancellationToken = default)
ReadReportsAsync(CancellationToken = default) -> IAsyncEnumerable<ExecutionReport>
```

All command times are explicit caller inputs. submittedAt must not precede CreatedAt. requestedAt
is request time, never evidence of cancellation. The interface contains no autonomous clock or ID
generation and imposes no wall-clock requirement on future historical simulation.

Normal completion of SubmitAsync means that the initial operational response has been normalized
and its Accepted or Rejected fact made available in the report stream. It does not mean acceptance,
fill or Order completion. There is no boolean, OrderStatus or second Accepted/Rejected return value.

Normal completion of RequestCancelAsync means that effective cancellation has been confirmed and
a Cancelled fact made available in the stream. Sending a request or receiving an intermediate
acknowledgment is insufficient. Application alone will decide when to call Order.Cancel.
Explicit cancellation refusal produces CancellationRefused; a technical interruption can preserve
an uncertain outcome. Neither manufactures a Cancelled report.

The consumer must process reports independently while command Tasks are pending. It must not block
the report owner waiting for a command whose completion depends on report publication. Reports can
be processed before the corresponding Task continuation. Only the stream owns lifecycle facts.

Equivalent resubmission within the execution scope denotes the same intent; conflicting content
under one OrderId must fail without resending. This does not promise external broker idempotency
or restart safety. Application must reconcile an uncertain outcome before retrying and permits
only one pending cancellation request per Order.

## Closed normalized facts

ExecutionReport is a sealed immutable class with private constructors and four factories, following
the repository's `From...` convention. No public constructor, setter, initializer or record `with`
can supply arbitrary Kind/payload combinations.

| Factory | Kind | OrderId source | Fill |
|---|---|---|---|
| FromAccepted | Accepted | Required argument | Null |
| FromRejected | Rejected | Required argument | Null |
| FromFill | Fill | Derived from required OrderFill | Exact original OrderFill |
| FromCancelled | Cancelled | Required argument | Null |

All factories require ObservedAt. Non-fill factories also require OrderId. Fill accepts only
OrderFill and ObservedAt, so an independently supplied mismatching OrderId is impossible.
Rejected means confirmed execution-provider business rejection; it carries no Domain rejection
reason. Future Application maps it to ExecutionProviderRejected. Cancelled means effective
cancellation, not pending cancellation or an unsuccessful request.

OrderFill crosses this boundary directly. ExecutionId, quantity, price and ExecutedAt are not
duplicated in another DTO. The producer owns stable deterministic generation or durable external
mapping of ExecutionId. These contracts never generate an identity. Broker order IDs, execution
strings and raw responses remain adapter-side.

## Time and lifecycle ownership

ObservedAt is the normalizer's observation instant. It is independent of OrderFill.ExecutedAt,
Application receipt time and the explicit at supplied to an Order transition. The contracts do not
require equality or order between observation and occurrence times. A mandatory fabricated
OccurredAt is not introduced for non-fill facts.

Future Application calls Order.Accept, Reject, ApplyFill or Cancel using its own explicit canonical
application time. Historical execution uses scheduler/virtual time, including explicit causal
ordering for events sharing a timestamp. None of these six types calls any Order transition.

## Stream, ordering and duplicates

One provider instance belongs to an execution scope with one logical report consumer, started before
commands. Reports are correlated by OrderId; there is no global stream registry. Application owns
serial access to each Order. Concurrency infrastructure is not implemented.

The provider must normalize a consistent per-Order lifecycle sequence: acceptance precedes new
fills; initial rejection need not follow acceptance; cancellation requires confirmation. External
out-of-order protocol messages are a future adapter concern. Terminal conflicts require explicit
reconciliation and must not weaken Order guards. Cross-Order arrival order has no lifecycle meaning.

Duplicates may occur. Redelivery of one economic fill must retain ExecutionId and equivalent
canonical content, including ExecutedAt. Domain's existing duplicate/conflict protection remains
authoritative. Future Application workflow suppresses repeated non-fill effective transitions;
ObservedAt of another delivery is not a new lifecycle decision. No deduplication is implemented here.

There is no guarantee of exactly-once delivery, durability or reconnection. Future implementations
must make buffering/failure policy explicit rather than silently lose facts. Canceling enumeration
stops consumption, not external Orders. Pending commands and uncertain outcomes must remain
operationally visible to the future owner during shutdown.

## Operational failures

The seven failure kinds are Unavailable, TransportFailure, Timeout, InvalidProviderResponse,
AdapterFailure, CancellationRefused and OutcomeUnknown. There is no Rejected failure kind.

ExecutionProviderException exposes FailureKind, trimmed ProviderKey, optional OrderId,
explicit OutcomeUncertain and the original caller-supplied sanitized Message. Undefined failure
kinds and null/blank keys or messages are argument errors. Null OrderId represents a session-level
failure without an invented sentinel. Message text is not concatenated with protocol data.

The producer supplies correct uncertainty using dispatch evidence. The constructor does not infer
that evidence from a category. Timeout after possible send and OutcomeUnknown require uncertainty
at the producer; a failure proven before send need not. Interrupting a wait does not prove that the
external operation had no effect. Standard OperationCanceledException is appropriate for a command
proven canceled before dispatch, or canceled stream consumption. After possible dispatch the command
must preserve uncertainty through the normalized failure contract.

No inner-exception overload is exposed. SDK exceptions and raw diagnostics remain private to a
future adapter; this normalized exception has no nested SDK object. The producer must sanitize
Message before construction: structural validation does not perform secret detection or redaction.

ExecutionProviderException never automatically implies Order.Reject. Only FromRejected represents
confirmed business rejection. CancellationRefused, timeout and transport failure never imply
Order.Cancel or a Cancelled report. Standard argument exceptions remain local programming errors.

## Deferred scope and preserved foundations

No concrete provider, matching, slippage, liquidity model, orchestrator, DI registration, repository,
migration, Account, Position, Risk, broker SDK, API or frontend feature is added. Generic execution
has no Market Data, Replay, Indicator or Chart dependency. Future Paper may consume causal market
observations privately; future broker adapters belong in Infrastructure.

Live integration requires a separate decision on authorized account context, durable mappings,
recovery, corrections/busts, new fills after terminal states and economic fills whose exact aggregate
or remainder cannot fit decimal. Those gates do not block this contract implementation.

All prior tracked files remain untouched, including Order and its exact quantity conservation.
EX-12 remains known: ADR-032's Accepted header coexists with an older editorial Proposed phrase.
This task does not clean up that document. ADR-033 remains Proposed pending review; no checkpoint
is performed and README Stack remains outside the task.
