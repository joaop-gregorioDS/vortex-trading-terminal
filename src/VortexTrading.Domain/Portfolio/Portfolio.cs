using System.Collections.ObjectModel;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Portfolio;

/// <summary>Immutable set of positions owned by one portfolio.</summary>
/// <remarks>
/// The portfolio knows only what is held and at what cost. It never consults a market: every valuation
/// takes the mark prices as an argument, which keeps valuation a pure function of supplied facts and
/// keeps this type free of any clock, provider or cache. That is the same discipline the execution
/// contracts follow, and it is what makes a portfolio value reproducible from stored fills.
/// <para>
/// A closed position is kept rather than removed. Its history matters — realized profit and the fact
/// that it was traded — so dropping it would erase information the caller cannot recover.
/// </para>
/// </remarks>
public sealed class Portfolio : IEquatable<Portfolio>
{
    private readonly ReadOnlyDictionary<InstrumentId, Position> _positions;

    private Portfolio(ReadOnlyDictionary<InstrumentId, Position> positions, UtcInstant? updatedAt)
    {
        _positions = positions;
        UpdatedAt = updatedAt;
    }

    /// <summary>Positions by instrument, including closed ones.</summary>
    public IReadOnlyDictionary<InstrumentId, Position> Positions => _positions;

    /// <summary>Time of the most recent applied fill, or null when nothing was ever applied.</summary>
    public UtcInstant? UpdatedAt { get; }

    /// <summary>An empty portfolio.</summary>
    public static Portfolio Empty() => new(
        new ReadOnlyDictionary<InstrumentId, Position>(new Dictionary<InstrumentId, Position>()), null);

    /// <summary>Builds a portfolio from positions, rejecting two entries for one instrument.</summary>
    public static Portfolio From(IEnumerable<Position>? positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var map = new Dictionary<InstrumentId, Position>();
        UtcInstant? updatedAt = null;
        foreach (var position in positions)
        {
            ArgumentNullException.ThrowIfNull(position);
            if (!map.TryAdd(position.InstrumentId, position))
            {
                throw new DomainException("A portfolio holds at most one position per instrument.");
            }

            if (position.UpdatedAt is not null
                && (updatedAt is null || position.UpdatedAt.CompareTo(updatedAt) > 0))
            {
                updatedAt = position.UpdatedAt;
            }
        }

        return new Portfolio(new ReadOnlyDictionary<InstrumentId, Position>(map), updatedAt);
    }

    /// <summary>Returns the position for an instrument, or an empty one when nothing is held.</summary>
    public Position PositionFor(InstrumentId? instrumentId, int moneyScale = Position.DefaultMoneyScale) =>
        _positions.TryGetValue(instrumentId ?? throw new ArgumentNullException(nameof(instrumentId)),
            out var position)
            ? position
            : Position.Empty(instrumentId, moneyScale);

    /// <summary>Applies one fill to the position of its instrument, creating it when absent.</summary>
    /// <remarks>
    /// The instrument is required because <see cref="OrderFill"/> does not carry one (ADR-038). A
    /// closed position stays in the portfolio: its realized result and its traded history remain.
    /// </remarks>
    public PortfolioFillResult Apply(InstrumentId? instrumentId, OrderFill? fill, OrderSide side,
        int moneyScale = Position.DefaultMoneyScale)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentNullException.ThrowIfNull(fill);

        var applied = PositionFor(instrumentId, moneyScale).Apply(fill, side);
        var positions = new Dictionary<InstrumentId, Position>(_positions)
        {
            [instrumentId] = applied.Position
        };

        return new PortfolioFillResult(From(positions.Values), applied.AppliedQuantity, applied.IgnoredQuantity);
    }

    /// <summary>Sum of the cost of every open position, at each position's own money scale.</summary>
    /// <remarks>
    /// Summing costs across instruments with different money scales is only meaningful when they share a
    /// currency and a scale. The caller decides that; this value is a plain sum of the stored costs.
    /// </remarks>
    public decimal TotalCost => _positions.Values.Aggregate(0m, (sum, position) =>
        ExactDecimal.Add(sum, position.TotalCost));

    /// <summary>Sum of realized profit and loss across every position, closed ones included.</summary>
    public decimal RealizedProfitAndLoss => _positions.Values.Aggregate(0m, (sum, position) =>
        ExactDecimal.Add(sum, position.RealizedProfitAndLoss));

    /// <summary>
    /// Market value of the open positions at the supplied marks, plus the cash supplied by the caller.
    /// </summary>
    /// <remarks>
    /// The portfolio does not hold cash. Equity is positions plus whatever cash the caller reports, so
    /// an instrument without a mark price is omitted instead of being valued at a fabricated price —
    /// silently marking at cost would hide precisely the movement the caller wants to see.
    /// </remarks>
    public decimal Equity(IReadOnlyDictionary<InstrumentId, Price>? markPrices, decimal cash = 0m)
    {
        ArgumentNullException.ThrowIfNull(markPrices);

        var equity = cash;
        foreach (var (instrumentId, position) in _positions)
        {
            if (!markPrices.TryGetValue(instrumentId, out var mark) || position.IsClosed)
            {
                continue;
            }

            equity = ExactDecimal.Add(equity, ExactDecimal.Multiply(position.Quantity, mark.Value, position.MoneyScale));
        }

        return equity;
    }

    /// <summary>Unrealized profit and loss across the open positions that have a mark price.</summary>
    public decimal UnrealizedProfitAndLoss(IReadOnlyDictionary<InstrumentId, Price>? markPrices)
    {
        ArgumentNullException.ThrowIfNull(markPrices);

        var total = 0m;
        foreach (var (instrumentId, position) in _positions)
        {
            if (!markPrices.TryGetValue(instrumentId, out var mark) || position.IsClosed)
            {
                continue;
            }

            total = ExactDecimal.Add(total, position.UnrealizedProfitAndLoss(mark));
        }

        return total;
    }

    /// <summary>Total profit and loss, realized plus unrealized, at the supplied marks.</summary>
    public decimal TotalProfitAndLoss(IReadOnlyDictionary<InstrumentId, Price>? markPrices) =>
        ExactDecimal.Add(RealizedProfitAndLoss, UnrealizedProfitAndLoss(markPrices));

    /// <summary>Instruments held with a non-zero quantity.</summary>
    public IEnumerable<InstrumentId> OpenInstrumentIds =>
        _positions.Where(entry => !entry.Value.IsClosed).Select(entry => entry.Key);

    public bool Equals(Portfolio? other)
    {
        if (other is null || _positions.Count != other._positions.Count)
        {
            return false;
        }

        return _positions.All(entry =>
            other._positions.TryGetValue(entry.Key, out var position) && position == entry.Value);
    }

    public override bool Equals(object? obj) => obj is Portfolio other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in _positions.OrderBy(entry => entry.Key.Value))
        {
            hash.Add(entry.Key);
            hash.Add(entry.Value);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(Portfolio? left, Portfolio? right) =>
        ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    public static bool operator !=(Portfolio? left, Portfolio? right) => !(left == right);
}
