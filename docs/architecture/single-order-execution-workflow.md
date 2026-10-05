# Single-Order Execution Workflow

## Scope and ownership

TASK 0.42 connects the checkpointed Order Domain and Execution Provider Boundary using one
public sealed Application type: `VortexTrading.Application.Trading.Execution.OrderExecutionWorkflow`.
Construction receives a Created Order and one provider with a stable, non-blank, trimmed identity.
It retains one canonical OrderSubmission built from the eight immutable intent components.
The provider never receives the mutable Order.

The caller hands off exclusive mutation ownership. All workflow entries and Order observation are
serial under one logical owner. The component is not thread-safe and adds no locks. Existing Order
references must not be mutated externally, and a new workflow must not be used to bypass unresolved
external effects. Contextual Instrument validation and future Risk gates belong before this handoff.

## Stream and lifetime

The execution-scope caller owns the single `ReadReportsAsync` consumer. It starts actual consumption
before commands, keeps consuming while commands are pending, routes by OrderId, supplies application
timestamps, observes returned command Tasks, and owns shutdown and stream failures. No buffering,
readiness handshake, durability or reconnect guarantee is added by this workflow.

The workflow never reads the stream. It contains no async reader, background continuation, Task.Run,
fire-and-forget task, clock, coordinator, pump, disposal contract or dependency registration.
GTC lifetime belongs to the scope; submission does not wait for Order terminality. Stream termination
while the Order is active is unresolved operational state. The caller must stop issuing commands;
ending consumption or canceling a wait does not cancel a trading Order.

## Public operations

```csharp
Task SubmitOrderAsync(UtcInstant submittedAt, CancellationToken cancellationToken = default);
Task RequestCancellationAsync(UtcInstant requestedAt, CancellationToken cancellationToken = default);
bool ApplyReport(ExecutionReport report, UtcInstant at);
```

Submit records an attempt before invoking the provider and always reuses the retained snapshot.
Normal Task completion means the initial Accepted/Rejected fact is available under the provider
contract, not that Application applied it. It never calls Order.Accept or Order.Reject.

Cancellation is allowed from Accepted/PartiallyFilled when healthy and no unresolved cancellation
attempt exists. Normal command completion means effective cancellation is available as a report;
it does not call Order.Cancel. A completed command awaiting that report still blocks another request.
Reports may be applied before either command Task completes, including acceptance followed by an
explicit cancellation request while submission completion remains pending.

## Operational command tracking

The workflow retains command Tasks and small private flags. It returns the provider Task directly;
an exception thrown synchronously by a provider becomes a faulted Task retaining the same exception.
Before subsequent serial operations, completed command outcomes are inspected exactly once without
blocking. No completion callback mutates workflow state on another execution path. The caller still
must await/observe every returned Task.

Technical exceptions never manufacture commercial facts. Normalized ExecutionProviderException is
preserved, including FailureKind and OutcomeUncertain. Unexpected exceptions are preserved and
treated conservatively as potentially effective operations. CancellationRefused does not change
Order; only a definitive refusal permits a future explicit new cancellation attempt.

No automatic retry exists. Explicit re-submission requires Created, a completed previous attempt
proved to have no external effect, no applied commercial fact, no uncertainty and healthy integrity.
Only normalized OutcomeUncertain=false or the boundary's pre-dispatch OperationCanceledException
establishes that absence. Providers must use uncertain normalized failures after possible dispatch;
the workflow does not reinterpret failure categories or parse exception messages.

Unknown submission blocks blind re-submission while still permitting later confirmed reports.
Unknown cancellation blocks another request while allowing fills and confirmed cancellation.
Applied facts take precedence over a subsequent command failure for retaining known commercial
history. Nothing clears uncertainty merely because time passes, and there is no recovery API.

## Report application and idempotency

Every report must target the owned Order and have a submission context with possible external effect.
No submitted context, or a definite no-effect submission without commercial facts, cannot authorize
provider lifecycle reports. A local cancellation request is not required for externally confirmed
cancellation. Created + Cancelled is valid only with this submission context.

| Report | Effective application | Equivalent redelivery |
|---|---|---|
| Accepted | Created -> Accept(at) | Private applied-acceptance marker; false even after later lifecycle evolution |
| Rejected | Created, or Accepted without fills -> Reject(ExecutionProviderRejected, at) | Private provider-rejection marker; false |
| Fill | Original OrderFill -> Order.ApplyFill(fill, at) | Domain ExecutionId/content authority returns false |
| Cancelled | Created/Accepted/PartiallyFilled -> Cancel(at) | Private applied-cancellation marker; false |

Non-fill markers are written only after Domain mutation succeeds. They represent successfully
applied facts, not a second lifecycle enum or event store. The workflow has no ExecutionId ledger.
Known equivalent fills remain Domain no-ops after Filled/Cancelled, including older application
times; null input time is never permitted. Duplicate non-fill reports also leave timestamps intact.

Fill before acceptance is not reordered or repaired. Rejection after execution and cancellation
after Filled/Rejected are protocol conflicts. A new fill after terminality or conflicting economic
content for a known ExecutionId is rejected by Domain. In a cancel/fill race, final fill preserves
Filled; subsequent Cancelled cannot reopen it. Effective cancellation preserves already applied
partial fills and remaining quantity.

## Time and failure integrity

The caller supplies submittedAt, requestedAt and application at. Submission time cannot precede
CreatedAt. Effective transitions retain Domain's at >= LastUpdatedAt rule, including equal times.
ObservedAt belongs to the provider normalizer; ExecutedAt belongs to the fill. Neither replaces at,
and the workflow does not consult wall clock or rewrite timestamps.

Wrong correlation and impossible non-fill contexts signal sanitized InvalidProviderResponse using
the existing ExecutionProviderException. DomainException is rethrown unchanged. Invalid report
application leaves the attempted Domain mutation unpublished and latches private integrity failure.
This includes regressive effective application time: the first slice conservatively stops rather
than offering timestamp repair/replay. Null arguments are rejected before processing without
consuming a valid transition. Once integrity fails, later commands/reports are blocked with
InvalidOperationException; there is no reset. Operational command failure alone does not latch
report integrity failure, so later valid facts remain usable.

## Dependencies and deferred work

Only existing Application execution contracts and Domain values are used. No new package or DI
registration is required. There are no Account, Position, Risk, persistence, Market Data, Replay,
indicator, API or frontend dependencies. Paper matching/pricing/liquidity is outside this component.

A future coordinator may own one scope consumer and dispatch OrderId -> OrderExecutionWorkflow.
Production hosting, readiness, durable command/report history, reconciliation, reconnect and crash
recovery require separate design before external execution. The in-memory workflow cannot recover
after process loss and does not promise exactly-once delivery.

## Validation

Hand-written test-only providers capture commands, return controlled Tasks and throw scripted
failures. Tests directly deliver canonical reports with explicit times and forbid workflow reads.
Coverage includes pending-command reports, safe explicit retry, uncertainty, idempotency, economic
guard integration, terminal conflicts, cancel/fill races and repeatable serial execution.
The approved TASK 0.42-A namespace guard permits exactly the six required boundary contracts plus
this workflow; no additional public execution type is authorized.

## References

- ADR-032 — Minimal Order Domain Foundation, Accepted.
- ADR-033 — Execution Provider Boundary Contracts, Accepted.
- ADR-034 — Single-Order Execution Workflow, Accepted.
- TASK 0.41 approved caller-driven workflow design.
- TASK 0.42-A approved namespace contract unlock.
