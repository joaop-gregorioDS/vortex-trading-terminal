using System.Globalization;
using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Trading;

public sealed record Money
{
    public decimal Amount { get; }
    public Currency Currency { get; }

    public Money(decimal amount, Currency? currency)
    {
        if (currency is null)
        {
            throw new DomainException("Money must have a currency.");
        }

        Amount = amount;
        Currency = currency;
    }

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency}";

    private static void EnsureSameCurrency(Money? left, Money? right)
    {
        if (left is null || right is null)
        {
            throw new DomainException("Money operations require non-null values.");
        }

        if (left.Currency != right.Currency)
        {
            throw new DomainException("Money operations require the same currency.");
        }
    }
}
