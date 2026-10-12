namespace VortexTrading.Application.Risk;

/// <summary>Evaluates pre-trade risk policies across a collection of rules.</summary>
public interface IRiskEvaluator
{
    /// <summary>Evaluates all configured rules for the given order submission context.</summary>
    RiskDecision Evaluate(RiskEvaluationContext context);
}
