using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VoltGrid.Contracts.Station.V1;
using VoltGrid.Infrastructure.Persistence;
using Google.Protobuf.WellKnownTypes;
using VoltGrid.Domain;

namespace VoltGrid.Gateway;

public class StationLinkService : StationLink.StationLinkBase
{
    private readonly ILogger<StationLinkService> _logger;
    private readonly IDbContextFactory<VoltGridDbContext> _dbFactory;
    private const int HeartbeatIntervalSeconds = 30;

    public StationLinkService(
        ILogger<StationLinkService> logger,
        IDbContextFactory<VoltGridDbContext> dbFactory)
    {
        _logger = logger;
        _dbFactory = dbFactory;
    }

    public override async Task Connect(
        IAsyncStreamReader<StationMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream, ServerCallContext context)
    {   
        var stationId = context.RequestHeaders.GetValue("station-id");
        if (string.IsNullOrWhiteSpace(stationId))
        {
            _logger.LogWarning("Stream rejected: missing station-id header, peer {Peer}", context.Peer);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "station-id is required"));
        }
        RegistrationStatus? streamBootStatus = null;
        try
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                _logger.LogInformation(
                    "Message {MessageId} from {StationId} received, payload {PayloadCase}",
                    message.MessageId,
                    stationId,
                    message.PayloadCase);
                switch (message.PayloadCase)
                {
                    case StationMessage.PayloadOneofCase.BootNotification:
                        var boot = message.BootNotification;
                        _logger.LogInformation("Boot from {StationId}, model {Model}, serial {SerialNumber}, firmware {FirmwareVersion}", stationId, boot.Model, boot.SerialNumber, boot.FirmwareVersion);
                        RegistrationStatus  status;
                        try
                        {
                            status = await RegisterBootAsync(stationId, boot, context.CancellationToken);
                        }
                        catch (Exception ex) when (ex is NpgsqlException || ex.InnerException is NpgsqlException)
                        {
                            _logger.LogError(ex, "Boot of {StationId} postponed: database unavailable", stationId);
                            status = RegistrationStatus.Pending;
                        }
                        streamBootStatus = status;
                        var response = new ServerMessage
                        {
                            MessageId = message.MessageId,
                            BootNotificationResponse = new BootNotificationResponse
                            {
                                Status = status,
                                HeartbeatIntervalSeconds = HeartbeatIntervalSeconds,
                                CurrentTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
                            }
                        };
                        await responseStream.WriteAsync(response);
                        break;
                    
                    case StationMessage.PayloadOneofCase.Heartbeat:
                    {
                        bool admitted;
                        if (streamBootStatus is RegistrationStatus.Rejected or RegistrationStatus.Pending)
                        {
                            admitted = false;
                        }
                        else
                        {
                            try
                            {
                                admitted = await RecordHeartbeatAsync(
                                    stationId, streamBootStatus == RegistrationStatus.Accepted,
                                    context.CancellationToken);
                            }
                            catch (Exception ex) when (ex is NpgsqlException || ex.InnerException is NpgsqlException)
                            {
                                _logger.LogError(ex, "Heartbeat of {StationId} not recorded: database unavailable",
                                    stationId);
                                admitted = streamBootStatus == RegistrationStatus.Accepted;
                            }
                        }

                        var reply = new ServerMessage { MessageId = message.MessageId };
                        if (admitted)
                        {
                            reply.HeartbeatResponse = new HeartbeatResponse { CurrentTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow) };
                        }
                        else
                        {
                            _logger.LogWarning("Message {MessageId} from {StationId} refused: station not accepted, stream status {StreamBootStatus}", message.MessageId, stationId, streamBootStatus);
                            reply.Error = new ErrorResponse { Code = ErrorCode.NotAccepted };
                        }

                        await responseStream.WriteAsync(reply);
                        break;
                    }

                    case StationMessage.PayloadOneofCase.StatusNotification:
                    {
                        var notification = message.StatusNotification;
                        var connectorStatus = ToDomain(notification.ConnectorStatus);
                        if (connectorStatus is ConnectorStatus.Unknown)
                        {
                            _logger.LogWarning("Station {StationId} connector {ConnectorNumber} reported unrecognised status {ReportedStatus}, recorded as Unknown", stationId, notification.ConnectorNumber, notification.ConnectorStatus);
                        }

                        ErrorCode? error = null;
                        if (streamBootStatus is RegistrationStatus.Rejected or RegistrationStatus.Pending)
                        {
                            error = ErrorCode.NotAccepted;
                        }
                        else
                        {
                            try
                            {
                                var admitted = await RecordConnectorStatusAsync(
                                    stationId,
                                    streamBootStatus == RegistrationStatus.Accepted,
                                    notification.ConnectorNumber,
                                    connectorStatus,
                                    context.CancellationToken);
                                if (!admitted)
                                {
                                    error = ErrorCode.NotAccepted;
                                }
                            }
                            catch (Exception ex) when (ex is NpgsqlException || ex.InnerException is NpgsqlException)
                            {
                                _logger.LogError(ex, "Status of {StationId} connector {ConnectorNumber} not recorded: database unavailable", stationId, notification.ConnectorNumber);
                                error = streamBootStatus == RegistrationStatus.Accepted ? ErrorCode.RetryLater : ErrorCode.NotAccepted;
                            }
                        }
                        
                        var reply = new ServerMessage { MessageId = message.MessageId };
                        if (error is null)
                        {
                            reply.StatusNotificationResponse = new StatusNotificationResponse();
                        }
                        else
                        {
                            _logger.LogWarning("Message {MessageId} from {StationId} refused: {Error}", message.MessageId, stationId, error);
                            reply.Error = new ErrorResponse { Code = error.Value };
                        }
                        
                        await responseStream.WriteAsync(reply);
                        break;
                    }
                    
                    default:
                        _logger.LogWarning("Unsupported payload {PayloadCase} in message {MessageId} from {StationId}", message.PayloadCase, message.MessageId, stationId);
                        break;
                }
            }

            _logger.LogInformation("Station {StationId} closed the stream", stationId);
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Station {StationId} connection lost: {Reason}",  stationId, ex.Message);
        }
        finally
        {
            _logger.LogInformation("Stream cleanup {StationId}", stationId); //Later gonna remove the station from Redis
        }
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
        _logger.LogInformation("Station {StationId} accepted",  stationId);
        return RegistrationStatus.Accepted;
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