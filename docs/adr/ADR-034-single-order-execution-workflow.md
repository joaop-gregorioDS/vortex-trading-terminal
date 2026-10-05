# ADR-034 — Single-Order Execution Workflow

- Status: Accepted
- Date: 2026-10-05
- Scope: TASK 0.42, including the approved TASK 0.42-A namespace correction

## Context

Checkpoint f584e9ba5bfbb9a61e54b777e9be5542a5f8a8f6 provides immutable execution submissions,
normalized reports and IExecutionProvider, while Order owns lifecycle and economic invariants.
TASK 0.41 approved the minimum Application bridge. A provider stream belongs to its execution scope
and requires one logical consumer active before commands. GTC Orders can remain active indefinitely;
command completion and business-fact application are separate events.

Order deduplicates fills by ExecutionId and canonical content, but non-fill transitions are not
idempotent. OrderStatus alone cannot distinguish a late duplicate acceptance from unknown acceptance.
Created can also mean submission pending or operationally uncertain, so transport state must not be
encoded into OrderStatus.

## Decision

Add exactly one public sealed production type, OrderExecutionWorkflow, in
VortexTrading.Application.Trading.Execution. It receives one Created Order and one IExecutionProvider,
validates provider identity, and retains one detached canonical OrderSubmission. Caller-provided
contextual validation precedes ownership handoff. Provider never receives the mutable Order.

Expose SubmitOrderAsync(submittedAt, token), RequestCancellationAsync(requestedAt, token) and
ApplyReport(report, at). Commands never apply lifecycle transitions. Reports do so through existing
Order methods, with correlated IDs and explicit application time. ObservedAt/ExecutedAt remain
independent; no clock abstraction or wall-clock access is introduced.

Retain minimal private command tracking, possible-effect/uncertainty flags and three successful
non-fill markers. Register attempts before invoking providers. Return provider Tasks without
background mutation; preserve synchronous provider errors in faulted Tasks. Completed results are
inspected on subsequent serial entries only. Caller observes every command and continues delivering
reports while commands await completion. No second fill ledger, public workflow state or result DTO
is introduced.

No automatic retry is allowed. Explicit retry requires confirmed absence of external effect, Created,
no commercial facts, no pending attempt and healthy integrity. OutcomeUncertain blocks blind retry;
valid later reports remain applicable. CancellationRefused never cancels Order; a definitive refusal
may permit a future explicit request. CancellationToken never means trading cancellation.

Wrong correlation/impossible non-fill context uses existing InvalidProviderResponse classification.
DomainException remains unchanged for economic/temporal guards. Protocol/data application failures
latch integrity and stop subsequent continuation, without recovery or silent repair. Equivalent fill
redelivery remains Domain-owned, including terminal duplicates. All accesses require one exclusive
serial logical owner; the workflow does not claim thread safety or add locks.

Caller/execution scope owns ReadReportsAsync, stream lifetime, routing, time selection and shutdown.
The workflow has no reader, loop, background task, coordinator, production pump or disposal contract.
Task completion does not assert that a report has already been applied and never waits for GTC
terminality. Stream termination is unresolved operational state, not rejection/cancellation.

Preserve the reviewed TASK 0.42-A test correction: six checkpointed contracts remain mandatory,
OrderExecutionWorkflow is the only additional allowed public type, and unknown expansion fails.
Do not change the existing six production contracts or any Domain foundation.

## Alternatives

A stateless handler displaces necessary submission/uncertainty and non-fill history into callers,
making ownership fragmented. It is rejected for this workflow.

A per-Order reader/session couples the workflow to the provider scope and requires explicit long-lived
background ownership for GTC. Readers per Order also conflict with a shared scope's single-consumer
contract. It is rejected in favor of caller-driven reports.

A provider-level coordinator is a suitable future multi-Order owner but adds routing, hosting and
shutdown infrastructure before the single-Order mechanics are validated. It is deferred.

## Consequences

- One authority applies Order lifecycle; provider operational outcomes cannot fabricate facts.
- Deterministic callers can supply virtual time and scripted reports without Paper implementation.
- Reports can precede command Task completion without blocking the single owner.
- Non-fill idempotency is in-memory history; Domain remains the only economic fill authority.
- Callers must enforce exclusive serial access and maintain the scope reader independently.
- No persistence, restart recovery, external-effect reconciliation or exactly-once promise is added.
- Integrity failure is conservative and permanent for this instance, including temporal guard failure.
- Only one production type and test-scope doubles are needed; no dependency/DI changes.
- Accepted at the TASK 0.42 checkpoint: the reviewed implementation and its test suite are the
  authority for this decision. Authorized expansions remain limited to the single type above.

## Deferred decisions

Paper provider/matching; coordinator and production pump; scope hosting/readiness; reconnect and
reconciliation; durable Order/workflow history and crash recovery; Account/Position/Risk; contextual
Instrument resolution; Market Data/Replay integrations; API/frontend; observability integration.

## References

- TASK 0.41 — Application Execution Orchestration Architecture Design, approved.
- TASK 0.42-A — Execution Namespace Contract Unlock, approved and preserved.
- ADR-032 and ADR-033, Accepted.
- docs/architecture/single-order-execution-workflow.md.
