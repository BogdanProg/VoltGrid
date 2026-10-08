using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoltGrid.Domain;

namespace VoltGrid.Infrastructure.Persistence.Configurations;

public class RfidTagConfiguration : IEntityTypeConfiguration<RfidTag>
{
    public void Configure(EntityTypeBuilder<RfidTag> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasMaxLength(20);
        builder.Property(r => r.OwnerName)
            .HasMaxLength(200);
    }
}