using Microsoft.EntityFrameworkCore;
using VoltGrid.Domain;

namespace VoltGrid.Infrastructure.Persistence;

public class VoltGridDbContext : DbContext
{
    public VoltGridDbContext(DbContextOptions<VoltGridDbContext> options)
        : base(options)
    {
    }
    public DbSet<ChargingStation> ChargingStations => Set<ChargingStation>();
    public DbSet<ChargingSession> ChargingSessions => Set<ChargingSession>();
    public DbSet<RfidTag> RfidTags => Set<RfidTag>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<Connector> Connectors => Set<Connector>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VoltGridDbContext).Assembly);
    }
}