# Minimal Order Domain Foundation

- Task: 0.38
- Date: 2026-10-05
- Architectural decision: ADR-032, Proposed pending checkpoint review.

## Purpose and scope

Represent a trading intention and its intrinsic lifecycle independently of market observations, matching, accounts, brokers and transport. Production code lives in `VortexTrading.Domain.Trading` within the existing Domain project, with no new packages or framework references.

The nine types are OrderId, ExecutionId, OrderSide, OrderType, TimeInForce, OrderStatus, OrderRejectionReason, OrderFill and Order. Order is a sealed entity with identity equality and guarded operations. IDs are immutable, non-empty caller-supplied GUIDs; neither identity type generates randomness.

`VortexTrading.Domain.Markets.Trade` remains a market observation. It is not an order execution or a fill. ExecutionId identifies a normalized economic fill occurrence, not a provider message or broker order. No complete Execution entity is introduced.

## Intent and price matrix

Intent properties are immutable: Id, InstrumentId, Side, Type, Quantity, LimitPrice, TimeInForce and CreatedAt. Order references only InstrumentId. Side is Buy or Sell; type is Market or Limit; the only supported TIF is GTC.

| Factory | Type | LimitPrice | Quantity |
|---|---|---|---|
| CreateMarket | Market | Absent; factory accepts no price | Positive |
| CreateLimit | Limit | Required and positive | Positive |

Both factories require non-null IDs, quantity and creation time, and defined Side/TIF values. The constructor is private. Existing Price and Quantity still permit zero globally; Order and OrderFill enforce stricter local positivity.

Construction produces Created, an empty fill collection, zero FilledQuantity, full RemainingQuantity, no rejection/completion and LastUpdatedAt equal to CreatedAt. Construction does not assert that a catalog has verified the instrument or that any executor has accepted the intention.

## Intrinsic versus contextual invariants

| Intrinsic, owned by Domain | Contextual, deferred to Application/executor |
|---|---|
| Required components and defined enums | Instrument existence and negotiability |
| Positive requested/fill quantities and applicable prices | Price/quantity increments |
| Market/Limit price matrix | Account, permissions, buying power and positions |
| Valid lifecycle and terminal protection | Market hours and execution capabilities |
| Fill targets this OrderId | Matching, liquidity, slippage and triggers |
| Side-specific limit price bound | Authorization of acceptance/cancellation |
| Stable fill identity and duplicate policy | External identity scope and correlation |
| Exact, representable cumulative and remaining quantities | Submission idempotency and asynchronous reconciliation |
| Explicit, nonregressive application timestamps | Serialization/persistence concurrency |

Order does not receive Instrument, duplicate metadata or validate increments. Future contextual submission may use Instrument.IsValidPrice and Instrument.IsValidQuantity without repairing invalid inputs by rounding.

## Lifecycle

Public operations are Accept(at), Reject(reason, at), Cancel(at) and bool ApplyFill(fill, at). All effective transitions require a non-null UtcInstant at greater than or equal to LastUpdatedAt. Equal application instants are valid.

| From | Operation | To | Additional requirements |
|---|---|---|---|
| Created | Accept | Accepted | Explicit application time |
| Created | Reject | Rejected | PreTradeValidationFailed or ExecutionProviderRejected |
| Accepted | Reject | Rejected | No fills; ExecutionProviderRejected only |
| Created | Cancel | Cancelled | Application owns authorization for local abandonment |
| Accepted | Cancel | Cancelled | Application owns confirmation of cancellation |
| PartiallyFilled | Cancel | Cancelled | Existing fills preserved |
| Accepted | ApplyFill | PartiallyFilled | New cumulative quantity below requested quantity |
| Accepted | ApplyFill | Filled | New cumulative quantity equals requested quantity |
| PartiallyFilled | ApplyFill | PartiallyFilled | New cumulative quantity below requested quantity |
| PartiallyFilled | ApplyFill | Filled | New cumulative quantity equals requested quantity |
| Any state with an applied ExecutionId | Equivalent ApplyFill | Unchanged | Return false; no timestamp change |

Every transition not listed is forbidden. Invalid operations throw DomainException. Rejection reason is a canonical enum, not arbitrary provider text. Partial orders cannot be rejected. The entity does not model submission, cancellation requests, expiry or replacement.

## Fill ownership and price guards

OrderFill is an immutable sealed value record with ExecutionId, OrderId, Quantity, Price and ExecutedAt. Every component is required; quantity and price are positive. Original primitive values, decimal representations and timestamps are preserved.

Order privately owns the insertion-ordered fill list and exposes a read-only wrapper. External consumers cannot add, clear or replace fills. FilledQuantity is derived from this list; RemainingQuantity is derived from requested quantity minus its exact sum. There is no independent cached accumulated quantity or stored remaining quantity.

New fills can be applied only in Accepted or PartiallyFilled. OrderId must match. Limit Buy requires fill.Price <= LimitPrice; Limit Sell requires fill.Price >= LimitPrice. Market imposes no execution price bound beyond positivity. The entity checks the submitted fact; it does not choose an execution price.

## ExecutionId and duplicate policy

ApplyFill requires non-null fill and application-time arguments. A fill targeting a different OrderId fails. Known execution identity is checked before state protection and timestamp monotonicity:

| Input | Result |
|---|---|
| New ExecutionId, valid active order and valid fill | Append once and return true |
| Known ExecutionId with equivalent content | Return false, including after Filled/Cancelled |
| Known ExecutionId with conflicting content | DomainException, including after terminal completion |
| New ExecutionId after a terminal state | DomainException |
| Different ExecutionIds with otherwise identical content | Two distinct fills |

Equivalent means the same IDs, numeric quantity, numeric price and normalized ExecutedAt. Numeric decimal scale differences and original UTC offsets do not create artificial inequality. An equivalent duplicate's non-null at may be older than LastUpdatedAt because no transition takes place.

Application/adapters must preserve a stable mapping from scoped external fill identity to ExecutionId. New IDs generated for every delivery would defeat this policy. Provider messages that carry several fills require individual normalized occurrences, not one shared message ID.

## Exact decimal conservation

All public quantities remain decimal-backed Quantity values. The implementation privately represents nonnegative quantities as BigInteger multiples of 10^-28, extracting the unsigned 96-bit decimal coefficient and its scale directly with decimal.GetBits. These BCL integers are implementation details, not new public numeric primitives.

The candidate cumulative quantity is summed exactly. It must not exceed requested quantity. Both that cumulative quantity and requested-minus-cumulative must be exactly representable as decimal before mutation.

Conversion removes only exact trailing decimal zeroes from the integer coefficient, with scale bounded by 0..28. If the resulting coefficient exceeds 96 bits, the operation throws DomainException. No significant digit is discarded. No floating point, rounding repair, saturation or tolerance is used. Derived Quantity representation may omit trailing zeroes; original supplied Quantity instances and their representation remain unchanged.

This detects OD-08: 1e20 + 1e-28 rounds to 1e20 with ordinary decimal addition, even under checked arithmetic. With requested quantity 1e20 + 1, a first fill of 1e20 is valid, but appending a fill of 1e-28 is rejected atomically. It also detects a representable tiny filled quantity whose subtraction from a large requested quantity would produce an unrepresentable exact remainder. Exact overfill is checked using integers before decimal conversion, so rounding cannot hide it.

The list remains the single source of truth; deriving quantities recomputes its exact sum. This is linear in fill count and does not introduce speculative caching or a shared mathematics library.

## Timestamp ownership and terminal semantics

CreatedAt and all operation at inputs use UtcInstant; Domain never queries a wall clock. ExecutedAt describes occurrence, while at describes canonical application to this entity. ExecutedAt can precede creation/application and can be out of order relative to other fills; Order does not impose assumptions about independent clocks or rewrite times.

Filled, Cancelled and Rejected are terminal. CompletedAt is derived as LastUpdatedAt in terminal states and absent otherwise. New effective operations after completion fail. Equivalent fill redelivery remains a no-op and does not move completion time.

Cancelled preserves executed quantity and fills. RemainingQuantity may remain positive and means unexecuted quantity, not working broker LeavesQty. Reject preserves a canonical reason and is only possible without fills.

## Atomicity, identity and concurrency

All required validation and exact candidate calculations finish before appending a fill or changing lifecycle state, rejection reason or application time. Failing operations preserve every observable property and the fill collection. A failed fill does not reserve its ExecutionId.

Order equality, operators and hashing depend only on OrderId, never mutable state. OrderFill has structural value equality. Entities are not thread-safe; future Application ownership must serialize operations for each OrderId. No version or sequence is introduced now.

## Deferred scope

Stop/StopLimit, Day/IOC/FOK, OCO groups, account/portfolio, positions, risk, fees, average fill price, replacement, corrections/busts, execution provider boundaries, matching/Paper Trading, API/UI, persistence, client submission idempotency and broker-specific fields are deferred. OCO is orchestration between independent orders, not an OrderType.

Existing Market Data, replay, indicators and chart foundations are unchanged and are not dependencies of Order. Integration with brokers will need explicit account scope, event ordering and reconciliation before it can safely handle fills after cancellation or execution corrections.

## Validation

Domain tests cover construction, enum boundaries, ID/equality semantics, every forbidden lifecycle combination, supported transitions, partial/full fills, side-specific limits, duplicate/conflicting execution IDs, terminal redelivery, explicit timestamps, encapsulation, deterministic input traces and atomic failures. Exactness regressions cover absorbed cumulative additions, unrepresentable remainders, masked overfill, coefficient boundaries, all decimal scales and valid mixed-scale fills.
