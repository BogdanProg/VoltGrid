using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoltGrid.Domain;

namespace VoltGrid.Infrastructure.Persistence.Configurations;

public class ChargingStationConfiguration : IEntityTypeConfiguration<ChargingStation>
{
    public void Configure(EntityTypeBuilder<ChargingStation> builder)
    {  
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasMaxLength(48);
        builder.Property(c => c.Name)
            .HasMaxLength(200);
        builder.Property(c => c.Address)
            .HasMaxLength(500);
        builder.Property(c => c.Model)
            .HasMaxLength(20);
        builder.Property(c => c.SerialNumber)
            .HasMaxLength(25);
    }
}