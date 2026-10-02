using System.Globalization;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Domain.Tests;

public sealed class InstrumentIdTests
{
    private static readonly Guid ExistingGuid = new("0d319028-b047-4e71-bd7d-9f6983dd17a4");

    [Fact]
    public void NewGeneratesANonEmptyIdentity()
    {
        var id = InstrumentId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void FromPreservesExistingIdentity()
    {
        Assert.Equal(ExistingGuid, InstrumentId.From(ExistingGuid).Value);
        Assert.Equal(ExistingGuid, new InstrumentId(ExistingGuid).Value);
    }

    [Fact]
    public void RejectsEmptyIdentityThroughConstructorAndFactory()
    {
        Assert.Throws<DomainException>(() => new InstrumentId(Guid.Empty));
        Assert.Throws<DomainException>(() => InstrumentId.From(Guid.Empty));
    }

    [Fact]
    public void EqualityAndHashingUseGuidValue()
    {
        var first = InstrumentId.From(ExistingGuid);
        var second = InstrumentId.From(ExistingGuid);
        var different = InstrumentId.From(new Guid("12067718-caa9-4b98-996d-b73234605d9f"));

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("pt-BR")]
    public void StringRepresentationUsesStableGuidFormat(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.Equal("0d319028-b047-4e71-bd7d-9f6983dd17a4", InstrumentId.From(ExistingGuid).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
