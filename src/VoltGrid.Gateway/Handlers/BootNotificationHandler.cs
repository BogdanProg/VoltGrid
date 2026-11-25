using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using VoltGrid.Contracts.Station.V1;
using VoltGrid.Infrastructure.Persistence;

namespace VoltGrid.Gateway.Handlers;

public class BootNotificationHandler
{
    private const int HeartbeatIntervalSeconds = 30;

    private readonly ILogger<BootNotificationHandler> _logger;
    private readonly IDbContextFactory<VoltGridDbContext> _dbFactory;

    public BootNotificationHandler(
        ILogger<BootNotificationHandler> logger,
        IDbContextFactory<VoltGridDbContext> dbFactory)
    {
        _logger = logger;
        _dbFactory = dbFactory;
    }
    
    public async Task<ServerMessage> HandleAsync(StreamState state, BootNotification boot, CancellationToken ct)
    {
        _logger.LogInformation("Boot from {StationId}, model {Model}, serial {SerialNumber}, firmware {FirmwareVersion}", state.StationId, boot.Model, boot.SerialNumber, boot.FirmwareVersion);

        RegistrationStatus status;
        try
        {
            status = await RegisterBootAsync(state.StationId, boot, ct);
        }
        catch (Exception ex) when (DatabaseErrors.IsUnavailable(ex))
        {
            _logger.LogError(ex, "Boot of {StationId} postponed: database unavailable", state.StationId);
            status = RegistrationStatus.Pending;
        }

        state.BootStatus = status;

        return new ServerMessage
        {
            BootNotificationResponse = new BootNotificationResponse
            {
                Status = status,
                HeartbeatIntervalSeconds = HeartbeatIntervalSeconds,
                CurrentTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
            }
        };
    }

    private async Task<RegistrationStatus> RegisterBootAsync(
        string stationId, BootNotification boot, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var station = await db.ChargingStations.FindAsync([stationId], ct);
        if (station is null)
        {
            _logger.LogWarning("Station {StationId} not found", stationId);
            return RegistrationStatus.Rejected;
        }

        if (boot.SerialNumber != station.SerialNumber)
        {
            _logger.LogWarning(
                "Station {StationId} rejected: serial number mismatch, database {Expected}, boot {Actual}",
                stationId, station.SerialNumber, boot.SerialNumber);
            station.RecordRejectedBoot();
            await db.SaveChangesAsync(ct);
            return RegistrationStatus.Rejected;
        }

        if (boot.Model != station.Model)
        {
            _logger.LogWarning(
                "Station {StationId} model mismatch, database {Expected}, boot {Actual}",
                stationId, station.Model, boot.Model);
        }

        station.RecordAcceptedBoot(boot.FirmwareVersion, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Station {StationId} accepted", stationId);
        return RegistrationStatus.Accepted;
    }
}
