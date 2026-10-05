using System.Globalization;
using System.Numerics;

namespace VortexTrading.Domain.Common;

/// <summary>Exact decimal arithmetic for financial derivations that <see cref="decimal"/> cannot do safely.</summary>
/// <remarks>
/// Multiplying or dividing two <see cref="decimal"/> values can overflow or round silently, and both
/// are unacceptable for cost basis, average price and P&amp;L (ADR-002). This helper carries every
/// operand as an exact <see cref="BigInteger"/> coefficient with an explicit power of ten, computes in
/// integers, and produces the single correctly rounded decimal at the end.
/// <para>
/// Two limits are documented rather than hidden. A result that is not exactly representable is
/// rounded once, half away from zero, at the caller-supplied result scale. A result whose magnitude
/// exceeds what a decimal can hold is rejected with <see cref="DomainException"/> instead of
/// overflowing or truncating silently.
/// </para>
/// </remarks>
public static class ExactDecimal
{
    /// <summary>Finest fractional scale a <see cref="decimal"/> can represent.</summary>
    public const int MaximumScale = 28;

    /// <summary>Largest number of significant digits a <see cref="decimal"/> coefficient can carry.</summary>
    private const int MaximumDigits = 29;

    private static readonly BigInteger MaximumCoefficient = (BigInteger.One << 96) - BigInteger.One;

    /// <summary>Sums two values exactly.</summary>
    /// <exception cref="DomainException">The exact sum is not representable as a decimal.</exception>
    public static decimal Add(decimal left, decimal right)
    {
        var (leftCoefficient, leftScale) = Decompose(left);
        var (rightCoefficient, rightScale) = Decompose(right);
        var scale = Math.Max(leftScale, rightScale);
        var coefficient = leftCoefficient * BigInteger.Pow(10, scale - leftScale)
            + rightCoefficient * BigInteger.Pow(10, scale - rightScale);
        return Reduce(coefficient, scale);
    }

    /// <summary>Subtracts exactly.</summary>
    /// <exception cref="DomainException">The exact difference is not representable as a decimal.</exception>
    public static decimal Subtract(decimal left, decimal right)
    {
        var (leftCoefficient, leftScale) = Decompose(left);
        var (rightCoefficient, rightScale) = Decompose(right);
        var scale = Math.Max(leftScale, rightScale);
        var coefficient = leftCoefficient * BigInteger.Pow(10, scale - leftScale)
            - rightCoefficient * BigInteger.Pow(10, scale - rightScale);
        return Reduce(coefficient, scale);
    }

    /// <summary>Negates exactly.</summary>
    public static decimal Negate(decimal value)
    {
        var (coefficient, scale) = Decompose(value);
        return Reduce(-coefficient, scale);
    }

    /// <summary>Multiplies exactly, rounding once to <paramref name="resultScale"/> fractional digits.</summary>
    /// <exception cref="DomainException">The result is not representable, or the scale is invalid.</exception>
    public static decimal Multiply(decimal left, decimal right, int resultScale)
    {
        ValidateScale(resultScale);

        var (leftCoefficient, leftScale) = Decompose(left);
        var (rightCoefficient, rightScale) = Decompose(right);

        // The exact product carries the sum of both scales. It is rounded once, to resultScale.
        return Reduce(leftCoefficient * rightCoefficient, leftScale + rightScale, resultScale);
    }

    /// <summary>Divides exactly, rounding once to <paramref name="resultScale"/> fractional digits.</summary>
    /// <exception cref="DomainException">The divisor is zero, or the scale is invalid.</exception>
    public static decimal Divide(decimal dividend, decimal divisor, int resultScale)
    {
        ValidateScale(resultScale);

        var (dividendCoefficient, dividendScale) = Decompose(dividend);
        var (divisorCoefficient, divisorScale) = Decompose(divisor);
        if (divisorCoefficient.IsZero)
        {
            throw new DomainException("Exact decimal division requires a non-zero divisor.");
        }

        // dividend/divisor = (a/b) * 10^(divisorScale - dividendScale). Scaling the numerator by
        // resultScale yields a coefficient that already counts resultScale fractional digits, and the
        // single division below performs the only rounding.
        var exponent = divisorScale - dividendScale + resultScale;
        var numerator = exponent >= 0
            ? dividendCoefficient * BigInteger.Pow(10, exponent)
            : dividendCoefficient;
        var denominator = exponent >= 0
            ? divisorCoefficient
            : divisorCoefficient * BigInteger.Pow(10, -exponent);

        return Reduce(Round(numerator, denominator), exponent >= 0 ? resultScale : 0, resultScale);
    }

    /// <summary>Sums exact products, each rounded once at <paramref name="resultScale"/>.</summary>
    /// <remarks>
    /// Intended for weighted sums such as "fill quantity times fill price" accumulated over partial
    /// fills. Each term is rounded independently and the sum over the rounded terms is exact.
    /// </remarks>
    public static decimal SumOfProducts(IEnumerable<(decimal Left, decimal Right)> terms, int resultScale)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ValidateScale(resultScale);

        // Accumulate in one shared scale so the running total stays exact; only the final reduction
        // can round.
        var (total, totalScale) = (BigInteger.Zero, resultScale);
        foreach (var (left, right) in terms)
        {
            var (leftCoefficient, leftScale) = Decompose(left);
            var (rightCoefficient, rightScale) = Decompose(right);
            var product = Reduce(leftCoefficient * rightCoefficient, leftScale + rightScale, resultScale);
            var (productCoefficient, productScale) = Decompose(product);

            var scale = Math.Max(totalScale, productScale);
            total = total * BigInteger.Pow(10, scale - totalScale)
                + productCoefficient * BigInteger.Pow(10, scale - productScale);
            totalScale = scale;
        }

        return Reduce(total, totalScale, resultScale);
    }

    /// <summary>Compares two values exactly.</summary>
    public static int Compare(decimal left, decimal right)
    {
        var (leftCoefficient, leftScale) = Decompose(left);
        var (rightCoefficient, rightScale) = Decompose(right);
        var scale = Math.Max(leftScale, rightScale);
        return (leftCoefficient * BigInteger.Pow(10, scale - leftScale))
            .CompareTo(rightCoefficient * BigInteger.Pow(10, scale - rightScale));
    }

    /// <summary>Renders a value in plain notation with the exact digits it carries.</summary>
    public static string ToPlainString(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>Returns the scale a value was stored with, up to 28.</summary>
    public static int ScaleOf(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xff;

    /// <summary>Splits a value into its exact coefficient and scale, so value = coefficient * 10^-scale.</summary>
    private static (BigInteger Coefficient, int Scale) Decompose(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = (BigInteger)(uint)bits[0]
            + ((BigInteger)(uint)bits[1] << 32)
            + ((BigInteger)(uint)bits[2] << 64);
        var scale = (bits[3] >> 16) & 0xff;
        return ((bits[3] & int.MinValue) != 0 ? -coefficient : coefficient, scale);
    }

    /// <summary>
    /// Builds a decimal from a coefficient meaning <c>coefficient * 10^-scale</c>, reducing trailing
    /// zeros until the coefficient fits the digit budget a decimal allows.
    /// </summary>
    /// <remarks>
    /// The scale is an exponent, not a count of digits to drop. Trailing zeros are removed so an
    /// exactly representable value such as 0.5 is accepted instead of being rejected because its
    /// full-precision form needs more digits than a decimal coefficient can carry.
    /// </remarks>
    private static decimal Reduce(BigInteger coefficient, int scale)
    {
        while (scale > 0 && coefficient % 10 == 0 && Digits(coefficient) > MaximumDigits)
        {
            coefficient /= 10;
            scale--;
        }

        if (scale > MaximumScale || BigInteger.Abs(coefficient) > MaximumCoefficient)
        {
            throw new DomainException("Exact decimal result is not representable as a decimal.");
        }

        // The 96-bit decimal constructor takes an unsigned magnitude plus a separate sign flag. Passing
        // a negative coefficient makes it reinterpret two's-complement bits as a magnitude, which turns
        // any negative result into a huge positive one, so the magnitude must be made explicit.
        var magnitude = BigInteger.Abs(coefficient);
        var low = unchecked((int)(uint)(magnitude & uint.MaxValue));
        var middle = unchecked((int)(uint)((magnitude >> 32) & uint.MaxValue));
        var high = unchecked((int)(uint)((magnitude >> 64) & uint.MaxValue));
        return new decimal(low, middle, high, coefficient.Sign < 0, (byte)scale);
    }

    /// <summary>Reduces a coefficient at one scale, rounding once to a coarser or equal target scale.</summary>
    private static decimal Reduce(BigInteger coefficient, int scale, int resultScale)
    {
        if (scale <= resultScale)
        {
            // The value is coarser than the target: pad with zeros, which changes no digits.
            return Reduce(coefficient * BigInteger.Pow(10, resultScale - scale), resultScale);
        }

        var divisor = BigInteger.Pow(10, scale - resultScale);
        return Reduce(Round(coefficient, divisor), resultScale);
    }

    /// <summary>Integer division with half-away-from-zero rounding, using no floating point.</summary>
    private static BigInteger Round(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder.IsZero)
        {
            return quotient;
        }

        if (BigInteger.Abs(remainder) * 2 < BigInteger.Abs(denominator))
        {
            return quotient;
        }

        return numerator.Sign * denominator.Sign < 0 ? quotient - 1 : quotient + 1;
    }

    private static int Digits(BigInteger value) =>
        BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture).Length;

    private static void ValidateScale(int scale)
    {
        if (scale is < 0 or > MaximumScale)
        {
            throw new DomainException("Scale is outside the range a decimal can represent.");
        }
    }
}
