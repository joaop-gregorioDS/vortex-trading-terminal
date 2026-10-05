using System.Reflection;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.Trading.Execution;

public sealed class ExecutionReportTests
{
    private static readonly OrderId Id = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Observed = new(new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.Zero).AddTicks(19));

    [Theory]
    [InlineData(ExecutionReportKind.Accepted)]
    [InlineData(ExecutionReportKind.Rejected)]
    [InlineData(ExecutionReportKind.Cancelled)]
    public void NonFillFactoriesHaveOnlyTheRequiredCanonicalState(ExecutionReportKind kind)
    {
        var report = CreateNonFill(kind, Id, Observed);

        Assert.Same(Id, report.OrderId);
        Assert.Equal(kind, report.Kind);
        Assert.Same(Observed, report.ObservedAt);
        Assert.Null(report.Fill);
    }

    [Fact]
    public void FillDerivesOrderIdAndRetainsTheCompleteOriginalImmutablePayload()
    {
        var fill = CreateFill();
        var report = ExecutionReport.FromFill(fill, Observed);
        var retained = Assert.IsType<OrderFill>(report.Fill);

        Assert.Equal(ExecutionReportKind.Fill, report.Kind);
        Assert.Same(fill, retained);
        Assert.Same(fill.OrderId, report.OrderId);
        Assert.Same(Observed, report.ObservedAt);
        Assert.NotEqual(fill.ExecutedAt, report.ObservedAt);
        Assert.Equal(7.1234567890123456789012345678m, retained.Price.Value);
        Assert.Equal(0.0000000000000000000000000001m, retained.Quantity.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void ObservedTimeIsNotForcedToEqualOrFollowExecutionTime(int hourOffset)
    {
        var fill = CreateFill();
        var observed = new UtcInstant(fill.ExecutedAt.Value.AddHours(hourOffset));
        var report = ExecutionReport.FromFill(fill, observed);

        Assert.Same(observed, report.ObservedAt);
        Assert.Same(fill.ExecutedAt, report.Fill!.ExecutedAt);
    }

    [Theory]
    [InlineData(ExecutionReportKind.Accepted, "orderId")]
    [InlineData(ExecutionReportKind.Accepted, "observedAt")]
    [InlineData(ExecutionReportKind.Rejected, "orderId")]
    [InlineData(ExecutionReportKind.Rejected, "observedAt")]
    [InlineData(ExecutionReportKind.Cancelled, "orderId")]
    [InlineData(ExecutionReportKind.Cancelled, "observedAt")]
    public void NonFillFactoriesRejectMissingComponents(ExecutionReportKind kind, string parameter)
    {
        var error = Assert.Throws<ArgumentNullException>(() =>
            CreateNonFill(kind, parameter == "orderId" ? null : Id, parameter == "observedAt" ? null : Observed));

        Assert.Equal(parameter, error.ParamName);
    }

    [Theory]
    [InlineData("fill")]
    [InlineData("observedAt")]
    public void FillFactoryRejectsMissingComponents(string parameter)
    {
        var error = Assert.Throws<ArgumentNullException>(() => ExecutionReport.FromFill(
            parameter == "fill" ? null : CreateFill(), parameter == "observedAt" ? null : Observed));

        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void ReportVocabularyContainsExactlyTheFourApprovedFacts()
    {
        Assert.Equal(new[] { ExecutionReportKind.Accepted, ExecutionReportKind.Rejected,
            ExecutionReportKind.Fill, ExecutionReportKind.Cancelled }, Enum.GetValues<ExecutionReportKind>());
    }

    [Fact]
    public void PublicConstructionCannotCreateInvalidKindsOrMismatchedPayloads()
    {
        var type = typeof(ExecutionReport);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        Assert.All(type.GetProperties(), property => Assert.False(property.CanWrite));
        Assert.All(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic), field => Assert.True(field.IsInitOnly));

        var factories = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Equal(new[] { "FromAccepted", "FromCancelled", "FromFill", "FromRejected" },
            factories.Select(factory => factory.Name).Order().ToArray());
        Assert.All(factories, factory => Assert.DoesNotContain(factory.GetParameters(),
            parameter => parameter.ParameterType == typeof(ExecutionReportKind)));
        var fillFactory = Assert.Single(factories, factory => factory.Name == "FromFill");
        Assert.Equal(new[] { typeof(OrderFill), typeof(UtcInstant) },
            fillFactory.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [Fact]
    public void RepeatedExplicitFillInputsPreserveIdentityAndObservableState()
    {
        var firstFill = CreateFill();
        var secondFill = CreateFill();
        var first = ExecutionReport.FromFill(firstFill, Observed);
        var second = ExecutionReport.FromFill(secondFill, new UtcInstant(Observed.Value));

        Assert.NotSame(firstFill, secondFill);
        Assert.Equal(first.Fill, second.Fill);
        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(first.Kind, second.Kind);
        Assert.Equal(first.ObservedAt, second.ObservedAt);
        Assert.Equal(firstFill.ExecutionId, secondFill.ExecutionId);
    }

    private static ExecutionReport CreateNonFill(ExecutionReportKind kind, OrderId? orderId, UtcInstant? observedAt)
        => kind switch
        {
            ExecutionReportKind.Accepted => ExecutionReport.FromAccepted(orderId, observedAt),
            ExecutionReportKind.Rejected => ExecutionReport.FromRejected(orderId, observedAt),
            ExecutionReportKind.Cancelled => ExecutionReport.FromCancelled(orderId, observedAt),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static OrderFill CreateFill() => new(
        new ExecutionId(new Guid("33333333-3333-3333-3333-333333333333")), Id,
        new Quantity(0.0000000000000000000000000001m), new Price(7.1234567890123456789012345678m),
        new UtcInstant(Observed.Value.AddHours(-1)));
}
