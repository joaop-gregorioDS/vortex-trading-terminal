using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Portfolio;

/// <summary>Immutable position for one instrument, priced by average cost.</summary>
/// <remarks>
/// Average cost is the accounting method: a buy folds its cost into the position and leaves the
/// average unchanged in basis; a sell realizes the difference between its price and the average and
/// keeps that same average for the remainder. Every derivation goes through
/// <see cref="ExactDecimal"/> so cost basis, average price and realized P&amp;L are exact rather than
/// accumulated rounding (ADR-002).
/// <para>
/// The scale used to express money is explicit and supplied by the caller, defaulting to
/// <see cref="DefaultMoneyScale"/>. ADR-002 forbids assuming a universal scale for all assets, so the
/// value is a policy input rather than a hardcoded constant inside the arithmetic.
/// </para>
/// <para>
/// A sell can never push the position negative. If it exceeds the held quantity, the applied result
/// reports it and the excess is ignored; the caller decides how to treat that, because inventing a
/// short position is a trading policy this type must not assume.
/// </para>
/// </remarks>
public sealed class Position : IEquatable<Position>
{
    /// <summary>Default number of fractional digits used to express money for an instrument.</summary>
    public const int DefaultMoneyScale = 8;

    private Position(InstrumentId instrumentId, decimal quantity, decimal totalCost,
        decimal realizedProfitAndLoss, int moneyScale, UtcInstant? updatedAt)
    {
        InstrumentId = instrumentId;
        Quantity = quantity;
        TotalCost = totalCost;
        RealizedProfitAndLoss = realizedProfitAndLoss;
        MoneyScale = moneyScale;
        UpdatedAt = updatedAt;
    }

    public InstrumentId InstrumentId { get; }

    /// <summary>Units currently held. Zero means the position is closed.</summary>
    public decimal Quantity { get; }

    /// <summary>Cost of the units held, at <see cref="MoneyScale"/>.</summary>
    public decimal TotalCost { get; }

    /// <summary>Profit or loss already realized by sells, at <see cref="MoneyScale"/>.</summary>
    public decimal RealizedProfitAndLoss { get; }

    /// <summary>Scale used for every money value of this position.</summary>
    public int MoneyScale { get; }

    /// <summary>Time of the last applied fill, or null when the position is empty.</summary>
    public UtcInstant? UpdatedAt { get; }

    /// <summary>True when no units are held.</summary>
    public bool IsClosed => Quantity == 0m;

    /// <summary>Cost of one unit, or zero when the position is closed.</summary>
    /// <remarks>
    /// Total cost is authoritative and the average is derived for display and for realizing a sell.
    /// Deriving it here, rather than storing it, keeps a single source of truth for cost. The average
    /// is computed with a little extra precision and then reduced to <see cref="MoneyScale"/> so it
    /// never carries digits the caller's money scale does not authorize; the extra digits are capped
    /// because a decimal cannot hold more than <see cref="ExactDecimal.MaximumScale"/> of them.
    /// </remarks>
    public decimal AverageCost => IsClosed
        ? 0m
        : ExactDecimal.Divide(TotalCost, Quantity,
            Math.Min(MoneyScale + 2, ExactDecimal.MaximumScale));

    /// <summary>An empty position for an instrument.</summary>
    public static Position Empty(InstrumentId? instrumentId, int moneyScale = DefaultMoneyScale) =>
        new(instrumentId ?? throw new ArgumentNullException(nameof(instrumentId)), 0m, 0m, 0m,
            ValidateScale(moneyScale), null);

    /// <summary>Applies one execution fill and returns the resulting position.</summary>
    /// <remarks>
    /// The result also reports the quantity the fill intended to sell and could not, so the caller is
    /// never left guessing whether an excess was dropped.
    /// </remarks>
    public PositionFillResult Apply(OrderFill? fill, OrderSide side)
    {
        ArgumentNullException.ThrowIfNull(fill);
        if (!Enum.IsDefined(side))
        {
            throw new DomainException("Applying a fill requires a supported side.");
        }

        // OrderFill carries no instrument: a fill belongs to an order, which knows its instrument. The
        // caller owns that contextual resolution, so this type only enforces that it applies to itself.
        return side == OrderSide.Buy ? ApplyBuy(fill) : ApplySell(fill);
    }

    private PositionFillResult ApplyBuy(OrderFill fill)
    {
        var added = ExactDecimal.Multiply(fill.Quantity.Value, fill.Price.Value, MoneyScale);
        var quantity = ExactDecimal.Add(Quantity, fill.Quantity.Value);
        var totalCost = ExactDecimal.Add(TotalCost, added);

        return new PositionFillResult(
            new Position(InstrumentId, quantity, totalCost, RealizedProfitAndLoss, MoneyScale, fill.ExecutedAt),
            fill.Quantity.Value,
            0m);
    }

    private PositionFillResult ApplySell(OrderFill fill)
    {
        var held = Quantity;
        var sold = ExactDecimal.Compare(fill.Quantity.Value, held) > 0 ? held : fill.Quantity.Value;
        var excess = ExactDecimal.Subtract(fill.Quantity.Value, sold);

        if (sold == 0m)
        {
            return new PositionFillResult(this, 0m, excess);
        }

        // Realized P&L is (price - average cost) * sold, and the remaining cost is the average cost of
        // what is left, which is what keeps the average unchanged after a sell.
        var average = ExactDecimal.Divide(TotalCost, held,
            Math.Min(MoneyScale + 2, ExactDecimal.MaximumScale));
        var proceedsDifference = ExactDecimal.Subtract(fill.Price.Value, average);
        var realized = ExactDecimal.Multiply(proceedsDifference, sold, MoneyScale);
        var remainingCost = ExactDecimal.Subtract(TotalCost, ExactDecimal.Multiply(average, sold, MoneyScale));
        var remaining = ExactDecimal.Subtract(held, sold);

        // A fully closed position must not carry a cost remainder caused by rounding the average.
        if (remaining == 0m)
        {
            remainingCost = 0m;
        }

        return new PositionFillResult(
            new Position(InstrumentId, remaining, remainingCost,
                ExactDecimal.Add(RealizedProfitAndLoss, realized), MoneyScale, fill.ExecutedAt),
            sold,
            excess);
    }

    /// <summary>Unrealized profit or loss at a mark price, at <see cref="MoneyScale"/>.</summary>
    /// <remarks>
    /// A closed position has no unrealized result: its outcome is already in
    /// <see cref="RealizedProfitAndLoss"/>.
    /// </remarks>
    public decimal UnrealizedProfitAndLoss(Price? markPrice)
    {
        ArgumentNullException.ThrowIfNull(markPrice);
        if (IsClosed)
        {
            return 0m;
        }

        var marketValue = ExactDecimal.Multiply(Quantity, markPrice.Value, MoneyScale);
        return ExactDecimal.Subtract(marketValue, TotalCost);
    }

    /// <summary>Total profit or loss at a mark price, realized plus unrealized.</summary>
    public decimal TotalProfitAndLoss(Price? markPrice) =>
        ExactDecimal.Add(RealizedProfitAndLoss, UnrealizedProfitAndLoss(markPrice));

    public bool Equals(Position? other) => other is not null
        && InstrumentId == other.InstrumentId
        && Quantity == other.Quantity
        && TotalCost == other.TotalCost
        && RealizedProfitAndLoss == other.RealizedProfitAndLoss
        && MoneyScale == other.MoneyScale;

    public override bool Equals(object? obj) => obj is Position other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(InstrumentId, Quantity, TotalCost,
        RealizedProfitAndLoss, MoneyScale);

    public static bool operator ==(Position? left, Position? right) =>
        ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    public static bool operator !=(Position? left, Position? right) => !(left == right);

    private static int ValidateScale(int moneyScale)
    {
        if (moneyScale is < 0 or > ExactDecimal.MaximumScale)
        {
            throw new DomainException("Money scale is outside the range a decimal can represent.");
        }

        return moneyScale;
    }
}
