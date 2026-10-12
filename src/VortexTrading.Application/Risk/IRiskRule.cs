namespace VortexTrading.Application.Risk;

/// <summary>Single pre-trade risk check evaluated before an order reaches an execution provider.</summary>
public interface IRiskRule
{
    /// <summary>Unique name of the risk rule for diagnostics and logging.</summary>
    string Name { get; }

    /// <summary>Evaluates the order in the given trading context.</summary>
    RiskDecision Evaluate(RiskEvaluationContext context);
}
