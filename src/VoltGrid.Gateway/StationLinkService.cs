using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VoltGrid.Contracts.Station.V1;
using VoltGrid.Infrastructure.Persistence;

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
                        var response = new ServerMessage
                        {
                            MessageId = message.MessageId,
                            BootNotificationResponse = new BootNotificationResponse
                            {
                                Status = status,
                                HeartbeatIntervalSeconds = HeartbeatIntervalSeconds 
                            }
                        };
                        await responseStream.WriteAsync(response);
                        break;
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
            _logger.LogInformation("Stream cleanup {StationId}", stationId); //Later will remove the station from Redis
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
            return RegistrationStatus.Rejected;
        }

        if (boot.Model != station.Model)
        {
            _logger.LogWarning(
                "Station {StationId} model mismatch, database {Expected}, boot {Actual}",
               stationId, station.Model, boot.Model);
        }
        station.RecordBoot(boot.FirmwareVersion, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Station {StationId} accepted",  stationId);
        return RegistrationStatus.Accepted;
    }
}