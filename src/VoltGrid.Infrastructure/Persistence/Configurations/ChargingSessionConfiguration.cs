using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoltGrid.Domain;

namespace VoltGrid.Infrastructure.Persistence.Configurations;

public class ChargingSessionConfiguration: IEntityTypeConfiguration<ChargingSession>
{
    public void Configure(EntityTypeBuilder<ChargingSession> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.StationId)
            .HasMaxLength(48);
        builder.Property(c => c.TransactionId)
            .HasMaxLength(36);
        builder.Property(c => c.RfidTagId)
            .HasMaxLength(20);
        builder.Property(c => c.PricePerKwh)
            .HasPrecision(10, 4);
        builder.Property(c => c.MeterStartWh)
            .HasPrecision(15, 3);
        builder.Property(c => c.MeterStopWh)
            .HasPrecision(15, 3);
        builder.HasOne<Connector>()
            .WithMany()
            .HasForeignKey(s => new { s.StationId, s.ConnectorNumber })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tariff>()
            .WithMany()
            .HasForeignKey(s => s.TariffId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RfidTag>()
            .WithMany()
            .HasForeignKey(s => s.RfidTagId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}