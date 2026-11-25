using Grpc.Core;
using VoltGrid.Contracts.Station.V1;
using VoltGrid.Gateway.Handlers;

namespace VoltGrid.Gateway;

public class StationLinkService : StationLink.StationLinkBase
{
    private readonly ILogger<StationLinkService> _logger;
    private readonly BootNotificationHandler _boot;
    private readonly HeartbeatHandler _heartbeat;
    private readonly StatusNotificationHandler _status;

    public StationLinkService(
        ILogger<StationLinkService> logger,
        BootNotificationHandler boot,
        HeartbeatHandler heartbeat,
        StatusNotificationHandler status)
    {
        _logger = logger;
        _boot = boot;
        _heartbeat = heartbeat;
        _status = status;
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

        var state = new StreamState(stationId);
        try
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                _logger.LogInformation(
                    "Message {MessageId} from {StationId} received, payload {PayloadCase}",
                    message.MessageId,
                    stationId,
                    message.PayloadCase);

                var reply = message.PayloadCase switch
                {
                    StationMessage.PayloadOneofCase.BootNotification => await _boot.HandleAsync(state, message.BootNotification, context.CancellationToken),
                    StationMessage.PayloadOneofCase.Heartbeat => await _heartbeat.HandleAsync(state, context.CancellationToken),
                    StationMessage.PayloadOneofCase.StatusNotification => await _status.HandleAsync(state, message.StatusNotification, context.CancellationToken),
                    _ => null
                };

                if (reply is null)
                {
                    _logger.LogWarning("Unsupported payload {PayloadCase} in message {MessageId} from {StationId}", message.PayloadCase, message.MessageId, stationId);
                    continue;
                }

                if (reply.PayloadCase == ServerMessage.PayloadOneofCase.Error)
                {
                    _logger.LogWarning("Message {MessageId} from {StationId} refused: {Error}, stream boot status {StreamBootStatus}", message.MessageId, stationId, reply.Error.Code, state.BootStatus);
                }

                reply.MessageId = message.MessageId;
                await responseStream.WriteAsync(reply);
            }

            _logger.LogInformation("Station {StationId} closed the stream", stationId);
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Station {StationId} connection lost: {Reason}", stationId, ex.Message);
        }
        finally
        {
            _logger.LogInformation("Stream cleanup {StationId}", stationId);
        }
    }
}
