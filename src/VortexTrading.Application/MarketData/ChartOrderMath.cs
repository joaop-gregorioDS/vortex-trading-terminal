using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.MarketData;

/// <summary>
/// Chart Trading classification. The server decides limit or stop from the clicked price and the
/// latest chart close. The browser does not compare those prices.
/// </summary>
public static class ChartOrderMath
{
    /// <summary>
    /// A buy at or below the close is a limit; a buy above it is a stop. A sell at or above the close
    /// is a limit; a sell below it is a stop.
    /// </summary>
    public static OrderType Classify(OrderSide side, decimal price, decimal referenceClose)
    {
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (price <= 0m || referenceClose <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Chart prices must be positive.");
        }

        return side == OrderSide.Buy
            ? price <= referenceClose ? OrderType.Limit : OrderType.Stop
            : price >= referenceClose ? OrderType.Limit : OrderType.Stop;
    }

    /// <summary>Money of one chart order: price, quantity and the instrument point value.</summary>
    public static decimal Notional(decimal price, decimal quantity, decimal pointValue)
    {
        if (price <= 0m || quantity <= 0m || pointValue <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Chart notional inputs must be positive.");
        }

        return price * quantity * pointValue;
    }
}
