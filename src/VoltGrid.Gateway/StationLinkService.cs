using Grpc.Core;
using VoltGrid.Contracts.Station.V1;

namespace VoltGrid.Gateway;

public class StationLinkService : StationLink.StationLinkBase
{
    private readonly ILogger<StationLinkService> _logger;
    private const int HeartbeatIntervalSeconds = 30;

    public StationLinkService(ILogger<StationLinkService> logger)
    {
        _logger = logger;
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
                        var response = new ServerMessage
                        {
                            MessageId = message.MessageId,
                            BootNotificationResponse = new BootNotificationResponse
                            {
                                Status = RegistrationStatus.Accepted,
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
}