using VoltGrid.Domain;
using Microsoft.EntityFrameworkCore;

namespace VoltGrid.Infrastructure.Persistence.Seeding;

public class DevDataSeeder
{
    public static async Task SeedAsync(VoltGridDbContext db, CancellationToken ct = default)
    {
        if (await db.ChargingStations.AnyAsync(ct))
            return;

        var acTariff = new Tariff
        {
            Id = Guid.CreateVersion7(),
            Name = "AC Standard",
            PricePerKwh = 1.89m,
            Currency = "PLN",
        };
        acTariff.SetActive(true);

        var dcTariff = new Tariff
        {
            Id = Guid.CreateVersion7(),
            Name = "DC Standard",
            PricePerKwh = 2.79m,
            Currency = "PLN",
        };
        dcTariff.SetActive(true);
        
        var station1 = new ChargingStation
        {
            Id = "ST-001",
            Name = "First",
            Address = "Grabowki 470",
            Latitude = 50.0003,
            Longitude = 20.0210,
            Model = "Plus",
            SerialNumber = "123"
        };

        var station2 = new ChargingStation
        {
            Id = "ST-002",
            Name = "Second",
            Address = "Cystersow 26",
            Latitude = 50.063920,
            Longitude = 19.969692,
            Model = "Pro",
            SerialNumber = "456"
        };

        var station1Ac = new Connector
        {
            StationId = station1.Id,
            Number = 1,
            Type = ConnectorType.Type2,
            MaxPowerWatts = 22_000,
            TariffId = acTariff.Id,
        };

        var station1Dc = new Connector
        {
            StationId = station1.Id,
            Number = 2,
            Type = ConnectorType.CcsCombo2,
            MaxPowerWatts = 150_000,
            TariffId = dcTariff.Id,
        };
        
        var station2Ac = new Connector
        {
            StationId = station2.Id,
            Number = 1,
            Type = ConnectorType.Type2,
            MaxPowerWatts = 22_000,
            TariffId = acTariff.Id,
        };

        var station2Dc = new Connector
        {
            StationId = station2.Id,
            Number = 2,
            Type = ConnectorType.CcsCombo2,
            MaxPowerWatts = 150_000,
            TariffId = dcTariff.Id,
        };

        var validTag = new RfidTag
        {
            Id = "01A1B2C3D4",
            OwnerName = "Test driver",
            ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
        };

        var blockedTag = new RfidTag
        {
            Id = "02A1B2C3D4",
            OwnerName = "Blocked driver",
            ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
        };
        blockedTag.SetBlocked(true);

        var expireTag = new RfidTag
        {
            Id = "03A1B2C3D4",
            OwnerName = "Expire driver",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        
        db.Tariffs.AddRange(acTariff, dcTariff);
        db.ChargingStations.AddRange(station1, station2);
        db.Connectors.AddRange(station1Ac, station1Dc, station2Ac, station2Dc);
        db.RfidTags.AddRange(validTag,blockedTag, expireTag);
        
        await db.SaveChangesAsync(ct);
    }
}