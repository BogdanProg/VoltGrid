using Microsoft.EntityFrameworkCore;
using VoltGrid.Contracts.Station.V1;
using VoltGrid.Domain;
using VoltGrid.Infrastructure.Persistence;

namespace VoltGrid.Gateway.Handlers;

public class StatusNotificationHandler
{
    private readonly ILogger<StatusNotificationHandler> _logger;
    private readonly IDbContextFactory<VoltGridDbContext> _dbFactory;

    public StatusNotificationHandler(
        ILogger<StatusNotificationHandler> logger,
        IDbContextFactory<VoltGridDbContext> dbFactory)
    {
        _logger = logger;
        _dbFactory = dbFactory;
    }

    public async Task<ServerMessage> HandleAsync(StreamState state, StatusNotification notification, CancellationToken ct)
    {
        var connectorStatus = ToDomain(notification.ConnectorStatus);
        if (connectorStatus is ConnectorStatus.Unknown)
        {
            _logger.LogWarning("Station {StationId} connector {ConnectorNumber} reported unrecognised status {ReportedStatus}, recorded as Unknown", state.StationId, notification.ConnectorNumber, notification.ConnectorStatus);
        }

        ErrorCode? error = null;
        if (state.RefusedInStream)
        {
            error = ErrorCode.NotAccepted;
        }
        else
        {
            try
            {
                var admitted = await RecordConnectorStatusAsync(
                    state.StationId,
                    state.AcceptedInStream,
                    notification.ConnectorNumber,
                    connectorStatus,
                    ct);
                if (!admitted)
                {
                    error = ErrorCode.NotAccepted;
                }
            }
            catch (Exception ex) when (DatabaseErrors.IsUnavailable(ex))
            {
                _logger.LogError(ex, "Status of {StationId} connector {ConnectorNumber} not recorded: database unavailable", state.StationId, notification.ConnectorNumber);
                error = state.AcceptedInStream ? ErrorCode.RetryLater : ErrorCode.NotAccepted;
            }
        }

        if (error is not null)
        {
            return new ServerMessage { Error = new ErrorResponse { Code = error.Value } };
        }

        return new ServerMessage { StatusNotificationResponse = new StatusNotificationResponse() };
    }

    private async Task<bool> RecordConnectorStatusAsync(
        string stationId, bool admittedInStream, int connectorNumber, ConnectorStatus status, CancellationToken ct)
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

        var connector = await db.Connectors.FindAsync([stationId, connectorNumber], ct);
        if (connector is null)
        {
            _logger.LogWarning("For {StationId} Connector {ConnectorNumber} not found, status {Status} can't be changed", stationId, connectorNumber, status);
            return true;
        }

        connector.ChangeStatus(status);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static ConnectorStatus ToDomain(ConnectorSentStatus status) => status switch
    {
        ConnectorSentStatus.Available => ConnectorStatus.Available,
        ConnectorSentStatus.Occupied => ConnectorStatus.Occupied,
        ConnectorSentStatus.Charging => ConnectorStatus.Charging,
        ConnectorSentStatus.Faulted => ConnectorStatus.Faulted,
        ConnectorSentStatus.Unavailable => ConnectorStatus.Unavailable,
        _ => ConnectorStatus.Unknown
    };
}
