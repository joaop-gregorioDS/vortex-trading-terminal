using System.Reflection;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Application.Trading.Paper;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.Trading.Execution;

public sealed class IExecutionProviderContractTests
{
    [Fact]
    public void InterfaceExposesOnlyStableIdentityTwoCommandsAndOneReportStream()
    {
        var type = typeof(IExecutionProvider);
        Assert.True(type.IsInterface);
        var key = Assert.Single(type.GetProperties());
        Assert.Equal("ProviderKey", key.Name);
        Assert.Equal(typeof(string), key.PropertyType);
        Assert.False(key.CanWrite);

        var submit = type.GetMethod("SubmitAsync")!;
        Assert.Equal(typeof(Task), submit.ReturnType);
        Assert.Equal(new[] { typeof(OrderSubmission), typeof(UtcInstant), typeof(CancellationToken) },
            submit.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        var cancel = type.GetMethod("RequestCancelAsync")!;
        Assert.Equal(typeof(Task), cancel.ReturnType);
        Assert.Equal(new[] { typeof(OrderId), typeof(UtcInstant), typeof(CancellationToken) },
            cancel.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        var stream = type.GetMethod("ReadReportsAsync")!;
        Assert.Equal(typeof(IAsyncEnumerable<ExecutionReport>), stream.ReturnType);
        Assert.Equal(new[] { typeof(CancellationToken) },
            stream.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.All(new[] { submit, cancel, stream }, method => Assert.True(method.GetParameters()[^1].IsOptional));
        Assert.Equal(3, type.GetMethods().Count(method => !method.IsSpecialName));
    }

    [Fact]
    public void ProductionNamespaceContainsOnlyAuthorizedExecutionTypes()
    {
        var types = typeof(IExecutionProvider).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.Namespace == typeof(IExecutionProvider).Namespace)
            .OrderBy(type => type.FullName)
            .ToArray();

        // The execution boundary is a frozen checkpoint plus explicitly authorized bridges. Each
        // addition must be named here, so an accidental new public type fails the build instead of
        // silently widening the boundary.
        var authorized = new[]
        {
            "VortexTrading.Application.Trading.Execution.OrderExecutionWorkflow", // TASK 0.42, ADR-034
            "VortexTrading.Application.Trading.Execution.ExecutionCoordinator",   // TASK 3.0, ADR-037
            "VortexTrading.Application.Trading.Execution.IApplicationClock",      // TASK 3.0, ADR-037
            "VortexTrading.Application.Trading.Execution.IPaperMarketObserver"    // TASK 3.0, ADR-037
        };

        Assert.Equal(
            BoundaryTypes().Select(type => type.FullName).Concat(authorized).OrderBy(name => name).ToArray(),
            types.Select(type => type.FullName).ToArray());
    }

    [Fact]
    public void PaperExecutionNamespaceKeepsExecutionAndMarketDataTypesOut()
    {
        // ADR-036 authorizes a separate Trading.Paper namespace instead of widening the frozen
        // execution boundary. This guard fails if a market-data, execution-boundary or infrastructure
        // type is ever pulled into it.
        var paper = typeof(PaperMarketObservation);
        var types = paper.Assembly.GetTypes()
            .Where(type => type.IsPublic && type.Namespace == paper.Namespace)
            .ToArray();

        Assert.NotEmpty(types);
        Assert.DoesNotContain(types.SelectMany(PublicSignatureTypes), type =>
            type == typeof(Quote) || type == typeof(Trade) || type == typeof(Candle)
            || type == typeof(IExecutionProvider) || type == typeof(ExecutionReport)
            || type == typeof(OrderSubmission) || type.IsSubclassOf(typeof(ExecutionProviderException))
            || type.Namespace == typeof(IExecutionProvider).Namespace
            || type.Namespace?.StartsWith("VortexTrading.Infrastructure", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ProviderAndSubmissionCannotExposeOrRetainMutableOrder()
    {
        Assert.DoesNotContain(PublicSignatureTypes(typeof(IExecutionProvider)), type => type == typeof(Order));
        Assert.DoesNotContain(PublicSignatureTypes(typeof(OrderSubmission)), type => type == typeof(Order));
        Assert.DoesNotContain(typeof(OrderSubmission).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType == typeof(Order));
    }

    [Fact]
    public void BoundaryPublicSignaturesHaveNoInfrastructureMarketStreamIndicatorOrApiTypes()
    {
        var signatureTypes = BoundaryTypes().SelectMany(PublicSignatureTypes);
        Assert.DoesNotContain(signatureTypes, type =>
            type == typeof(Quote) || type == typeof(Trade) || type == typeof(Candle)
            || type.Namespace?.StartsWith("VortexTrading.Infrastructure", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("VortexTrading.Api", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("VortexTrading.Application.MarketData", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("VortexTrading.Application.Indicators", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("VortexTrading.Application.Charts", StringComparison.Ordinal) == true);
    }

    private static Type[] BoundaryTypes() =>
    [
        typeof(IExecutionProvider),
        typeof(OrderSubmission),
        typeof(ExecutionReport),
        typeof(ExecutionReportKind),
        typeof(ExecutionProviderFailureKind),
        typeof(ExecutionProviderException)
    ];

    private static IEnumerable<Type> PublicSignatureTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var direct = type.GetMethods(flags).SelectMany(method => method.GetParameters()
            .Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .Concat(type.GetConstructors().SelectMany(constructor => constructor.GetParameters()
                .Select(parameter => parameter.ParameterType)))
            .Concat(type.GetProperties(flags).Select(property => property.PropertyType));

        return direct.SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in Flatten(argument))
            {
                yield return nested;
            }
        }
    }
}
