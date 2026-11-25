using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using VoltGrid.Contracts.Station.V1;
using VoltGrid.Domain;
using VoltGrid.Infrastructure.Persistence;

namespace VoltGrid.Gateway.Handlers;

public class HeartbeatHandler
{
    private readonly ILogger<HeartbeatHandler> _logger;
    private readonly IDbContextFactory<VoltGridDbContext> _dbFactory;

    public HeartbeatHandler(
        ILogger<HeartbeatHandler> logger,
        IDbContextFactory<VoltGridDbContext> dbFactory)
    {
        _logger = logger;
        _dbFactory = dbFactory;
    }
    
    public async Task<ServerMessage> HandleAsync(StreamState state, CancellationToken ct)
    {
        bool admitted;
        if (state.RefusedInStream)
        {
            admitted = false;
        }
        else
        {
            try
            {
                admitted = await RecordHeartbeatAsync(state.StationId, state.AcceptedInStream, ct);
            }
            catch (Exception ex) when (DatabaseErrors.IsUnavailable(ex))
            {
                _logger.LogError(ex, "Heartbeat of {StationId} not recorded: database unavailable", state.StationId);
                admitted = state.AcceptedInStream;
            }
        }

        if (!admitted)
        {
            return new ServerMessage { Error = new ErrorResponse { Code = ErrorCode.NotAccepted } };
        }

        return new ServerMessage
        {
            HeartbeatResponse = new HeartbeatResponse { CurrentTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow) }
        };
    }

    private async Task<bool> RecordHeartbeatAsync(
        string stationId, bool admittedInStream, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var station = await db.ChargingStations.FindAsync([stationId], ct);
        if (station is null)
        {
            return false;
        }

        if (!admittedInStream && station.LastBootStatus != LastBootStatus.Accepted)
        {
            return false;
        }

        station.RecordHeartbeat(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
