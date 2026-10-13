using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace VortexTrading.Infrastructure.Persistence.Configurations;

/// <summary>Maps the minute tape of the market-watch catalog. Prices stay exact numerics.</summary>
internal sealed class MinuteBarConfiguration : IEntityTypeConfiguration<MinuteBarRow>
{
    public void Configure(EntityTypeBuilder<MinuteBarRow> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("minute_bars");
        builder.HasKey(bar => new { bar.InstrumentId, bar.OpenTime });

        builder.Property(bar => bar.OpenTime).HasColumnType("timestamp with time zone");
        builder.Property(bar => bar.Open).HasColumnType(PersistenceConverters.ExactNumeric);
        builder.Property(bar => bar.High).HasColumnType(PersistenceConverters.ExactNumeric);
        builder.Property(bar => bar.Low).HasColumnType(PersistenceConverters.ExactNumeric);
        builder.Property(bar => bar.Close).HasColumnType(PersistenceConverters.ExactNumeric);
        builder.Property(bar => bar.Volume).HasColumnType(PersistenceConverters.ExactNumeric);

        builder.HasIndex(bar => bar.OpenTime);
    }
}
