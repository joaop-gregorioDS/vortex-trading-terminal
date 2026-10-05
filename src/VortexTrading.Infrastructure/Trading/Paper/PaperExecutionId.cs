using System.Security.Cryptography;
using System.Text;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Infrastructure.Trading.Paper;

/// <summary>Derives stable execution identities for paper fills.</summary>
/// <remarks>
/// ADR-033 makes the producer responsible for deterministic identity generation, and ADR-036 requires
/// that the same order and fill sequence always yield the same identity. Deriving the identity by
/// hashing the provider key, the order and the sequence makes every paper scenario reproducible and
/// lets the domain deduplicate a redelivered fill by content instead of by luck. A random identifier
/// would make a repeated scenario look like a different execution and break that guarantee.
/// </remarks>
internal static class PaperExecutionId
{
    internal static ExecutionId From(string providerKey, OrderId orderId, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentNullException.ThrowIfNull(orderId);

        var material = $"{providerKey}|{orderId.Value:D}|{sequence}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var bytes = digest.AsSpan(0, 16).ToArray();

        // Mark the result as a version 5 (name-based) identifier with the RFC 4122 variant so the
        // value is a well-formed UUID, then force it non-empty: ExecutionId rejects Guid.Empty.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        if (Array.TrueForAll(bytes, value => value == 0))
        {
            bytes[15] = 1;
        }

        return new ExecutionId(new Guid(bytes));
    }
}
