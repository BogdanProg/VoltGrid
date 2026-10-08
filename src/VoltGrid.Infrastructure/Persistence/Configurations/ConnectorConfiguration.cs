using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoltGrid.Domain;

namespace VoltGrid.Infrastructure.Persistence.Configurations;

public class ConnectorConfiguration : IEntityTypeConfiguration<Connector>
{
    public void Configure(EntityTypeBuilder<Connector> builder)
    {
        builder.Property(c => c.StationId)
            .HasMaxLength(48);
        builder.HasKey(c => new { c.StationId, c.Number});
        builder.HasOne<ChargingStation>()
            .WithMany()
            .HasForeignKey(s =>s.StationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tariff>()
            .WithMany()
            .HasForeignKey(s => s.TariffId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}