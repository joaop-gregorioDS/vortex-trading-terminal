using VortexTrading.Domain.Common;

namespace VortexTrading.Domain.Trading;

public sealed record ExecutionId
{
    public Guid Value { get; }

    public ExecutionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("Execution ID must not be empty.");
        }

        Value = value;
    }

    public static ExecutionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}
