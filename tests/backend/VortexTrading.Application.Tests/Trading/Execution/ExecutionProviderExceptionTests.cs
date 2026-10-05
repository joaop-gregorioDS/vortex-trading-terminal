using System.Reflection;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.Trading.Execution;

public sealed class ExecutionProviderExceptionTests
{
    private static readonly OrderId Id = new(new Guid("11111111-1111-1111-1111-111111111111"));

    [Theory]
    [InlineData(ExecutionProviderFailureKind.Unavailable, false)]
    [InlineData(ExecutionProviderFailureKind.TransportFailure, true)]
    [InlineData(ExecutionProviderFailureKind.Timeout, true)]
    [InlineData(ExecutionProviderFailureKind.InvalidProviderResponse, true)]
    [InlineData(ExecutionProviderFailureKind.AdapterFailure, false)]
    [InlineData(ExecutionProviderFailureKind.CancellationRefused, false)]
    [InlineData(ExecutionProviderFailureKind.OutcomeUnknown, true)]
    public void PreservesNormalizedClassificationAndSanitizedMessage(ExecutionProviderFailureKind kind, bool uncertain)
    {
        const string message = "  Operation outcome requires reconciliation.  ";
        var error = new ExecutionProviderException(kind, "  Simulation.KEY  ", Id, uncertain, message);

        Assert.IsAssignableFrom<Exception>(error);
        Assert.Equal(kind, error.FailureKind);
        Assert.Equal("Simulation.KEY", error.ProviderKey);
        Assert.Same(Id, error.OrderId);
        Assert.Equal(uncertain, error.OutcomeUncertain);
        Assert.Equal(message, error.Message);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProducerExplicitlySuppliesTransportOutcomeUncertainty(bool uncertain)
    {
        var error = new ExecutionProviderException(ExecutionProviderFailureKind.TransportFailure,
            "simulation", Id, uncertain, "Transport operation interrupted.");

        Assert.Equal(uncertain, error.OutcomeUncertain);
    }

    [Fact]
    public void SessionFailureNeedsNoOrderIdOrSentinel()
    {
        var error = new ExecutionProviderException(ExecutionProviderFailureKind.Unavailable,
            "simulation", null, false, "Execution session unavailable.");

        Assert.Null(error.OrderId);
        Assert.False(error.OutcomeUncertain);
    }

    [Theory]
    [InlineData(" simulation ", "simulation")]
    [InlineData("\tSimulation.KEY\r\n", "Simulation.KEY")]
    [InlineData("simulation-key", "simulation-key")]
    public void ProviderKeyIsTrimmedWithoutChangingCase(string supplied, string expected)
    {
        var error = new ExecutionProviderException(ExecutionProviderFailureKind.AdapterFailure,
            supplied, null, false, "Normalization failed.");

        Assert.Equal(expected, error.ProviderKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0")]
    public void RejectsMissingOrBlankProviderKey(string? providerKey)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => new ExecutionProviderException(
            ExecutionProviderFailureKind.AdapterFailure, providerKey, null, false, "Normalization failed."));

        Assert.Equal("providerKey", error.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void RequiresAnExplicitNonBlankSanitizedMessage(string? message)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => new ExecutionProviderException(
            ExecutionProviderFailureKind.AdapterFailure, "simulation", null, false, message));

        Assert.Equal("message", error.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void RejectsUndefinedFailureKindWithoutCoercion(int kind)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionProviderException(
            (ExecutionProviderFailureKind)kind, "simulation", null, false, "Normalization failed."));

        Assert.Equal("failureKind", error.ParamName);
    }

    [Fact]
    public void VocabularyContainsExactlySevenOperationalCategoriesAndNoBusinessRejection()
    {
        Assert.Equal(new[] { ExecutionProviderFailureKind.Unavailable, ExecutionProviderFailureKind.TransportFailure,
            ExecutionProviderFailureKind.Timeout, ExecutionProviderFailureKind.InvalidProviderResponse,
            ExecutionProviderFailureKind.AdapterFailure, ExecutionProviderFailureKind.CancellationRefused,
            ExecutionProviderFailureKind.OutcomeUnknown }, Enum.GetValues<ExecutionProviderFailureKind>());
    }

    [Fact]
    public void NormalizedFailurePropertiesAreReadOnlyAndNoInnerExceptionIsAccepted()
    {
        var type = typeof(ExecutionProviderException);
        Assert.True(type.IsSealed);
        Assert.All(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            property => Assert.False(property.CanWrite));
        Assert.All(type.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => Assert.False(typeof(Exception).IsAssignableFrom(parameter.ParameterType)));
    }
}
