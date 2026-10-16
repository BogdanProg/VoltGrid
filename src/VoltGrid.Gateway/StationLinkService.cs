using Grpc.Core;
using VoltGrid.Contracts.Station.V1;

namespace VoltGrid.Gateway;

public class StationLinkService : StationLink.StationLinkBase
{
    private readonly ILogger<StationLinkService> _logger;

    public StationLinkService(ILogger<StationLinkService> logger)
    {
        _logger = logger;
    }

    public override async Task Connect(
        IAsyncStreamReader<StationMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream, ServerCallContext context)
    {
        try
            {
                await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    _logger.LogInformation(
                        "Message {MessageId} received, payload {PayloadCase}",
                        message.MessageId,
                        message.PayloadCase);
                }

                _logger.LogInformation("Station closed the stream");
            }
        catch(IOException ex)
        {
                _logger.LogWarning("Station connection lost: {Reason}", ex.Message);    
        }
        finally
        {
            _logger.LogInformation("Stream cleanup"); //Later will remove the station from Redis
        }
    }
}