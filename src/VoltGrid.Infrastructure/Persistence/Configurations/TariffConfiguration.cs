using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoltGrid.Domain;

namespace VoltGrid.Infrastructure.Persistence.Configurations;

public class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name)
            .HasMaxLength(100);
        builder.Property(p => p.PricePerKwh)
            .HasPrecision(10, 4);
        builder.Property(p =>p.Currency)
            .HasMaxLength(3);
    }
}