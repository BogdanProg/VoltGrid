using Grpc.Core;
using Grpc.Net.Client;
using VoltGrid.Contracts.Station.V1;

const string GatewayAddress = "http://localhost:5205";

const string StationId = "ST-001";

const string StationModel = "Plus";

const string StationSerialNumber = "123";

const string StationFirmwareVersion = "1.0.5";

const int ReplyTimeoutSeconds = 35; 

const int MinBootRetrySeconds = 25;

const int MinHeartbeatIntervalSeconds = 5;

const int StatusRetrySeconds = 15;

const int ReconnectDelaySeconds = 5;

int[] connectorNumbers = [1, 2];

var lastMessageId = 0;

using var channel = GrpcChannel.ForAddress(GatewayAddress);

var client = new StationLink.StationLinkClient(channel);

var headers = new Metadata { { "station-id", StationId } };

while (true)
{
    try
    {   
        Console.WriteLine($"Connecting at {DateTime.UtcNow}");
        using var call = client.Connect(headers);
        var bootResponse = await BootAsync(call);
        Console.WriteLine("Station accepted");
    
        foreach (var connectorNumber in connectorNumbers)
        {
            await ReportStatusAsync(call, connectorNumber, ConnectorSentStatus.Available);
        }   
        var heartbeatIntervalSeconds = Math.Max( bootResponse.HeartbeatIntervalSeconds , MinHeartbeatIntervalSeconds);
        Console.WriteLine($"Heartbeat every {heartbeatIntervalSeconds} s");
        await HeartbeatLoopAsync(call, heartbeatIntervalSeconds);
    }
    catch (IOException ex)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} : Connection lost: {ex.GetType().Name} : {ex.Message}");
    }
    
    catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.Cancelled or StatusCode.Unknown)
    {
        Console.WriteLine($"{ex.StatusCode}, {ex.Status.Detail}");
    }
    
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss}, {ex.Status.Detail}");
        return;
    }
    
    await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds));
}

async Task<ServerMessage> SendAsync(
    AsyncDuplexStreamingCall<StationMessage, ServerMessage> call,
    StationMessage message)
{
    var messageId =  ++lastMessageId;
    message.MessageId = messageId.ToString();
    await call.RequestStream.WriteAsync(message);
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} : Sent message {messageId}: {message.PayloadCase}");
    using var timeout =  new CancellationTokenSource(TimeSpan.FromSeconds(ReplyTimeoutSeconds));
    var hasReply = await call.ResponseStream.MoveNext(timeout.Token);
    if (!hasReply)
    {
        throw new RpcException(new Status(StatusCode.Unavailable, $"Gateway closed the stream without replying to message {messageId}"));
    }
    
    var reply = call.ResponseStream.Current;
    if (reply.MessageId != message.MessageId)
    {
        throw new InvalidOperationException($"Reply id {reply.MessageId} does not match message id {message.MessageId}");
    }
    
    return reply;
}
async Task<BootNotificationResponse> BootAsync(
    AsyncDuplexStreamingCall<StationMessage, ServerMessage> call)
{
    var bootMessage = new StationMessage
    {
        BootNotification = new BootNotification
        {
            Model = StationModel,
            SerialNumber = StationSerialNumber,
            FirmwareVersion = StationFirmwareVersion
        }
    };

    while (true)
    {
        var reply = await SendAsync(call, bootMessage);

        if (reply.PayloadCase != ServerMessage.PayloadOneofCase.BootNotificationResponse)
        {
            throw new InvalidOperationException($"Unexpected reply: {reply.PayloadCase}");
        }

        var bootResponse = reply.BootNotificationResponse;
        Console.WriteLine($"Boot {bootResponse.Status}, interval {bootResponse.HeartbeatIntervalSeconds} s");

        switch (bootResponse.Status)
        {
            case RegistrationStatus.Accepted:
                return bootResponse;

            case RegistrationStatus.Pending:
            case RegistrationStatus.Rejected:
                var retryAfterSeconds = Math.Max(bootResponse.HeartbeatIntervalSeconds, MinBootRetrySeconds); 
                Console.WriteLine($"Retrying boot in {retryAfterSeconds} s");
                await Task.Delay(TimeSpan.FromSeconds(retryAfterSeconds));
                break;

            default:
                throw new InvalidOperationException($"Unexpected boot status: {bootResponse.Status}");
        }
    }
}
async Task HeartbeatLoopAsync(
    AsyncDuplexStreamingCall<StationMessage, ServerMessage> call,
    int heartbeatIntervalSeconds)
{
    var heartbeatMessage = new StationMessage { Heartbeat = new Heartbeat() };

    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(heartbeatIntervalSeconds));

        var reply = await SendAsync(call, heartbeatMessage);

        if (reply.PayloadCase == ServerMessage.PayloadOneofCase.HeartbeatResponse)
        {
            Console.WriteLine($"Heartbeat response {reply.HeartbeatResponse.CurrentTime.ToDateTimeOffset()}");
        }
        else
        {
            throw new InvalidOperationException($"Unexpected reply to heartbeat response {reply}");
        }
    }
}
async Task ReportStatusAsync(
    AsyncDuplexStreamingCall<StationMessage, ServerMessage> call,
    int connectorNumber,
    ConnectorSentStatus status)
{
    var statusMessage = new StationMessage
    {
        StatusNotification = new StatusNotification
        {
            ConnectorNumber = connectorNumber,
            ConnectorStatus = status
        }
    };

    while (true)
    {
        var reply = await SendAsync(call, statusMessage);

        if (reply.PayloadCase == ServerMessage.PayloadOneofCase.StatusNotificationResponse)
        {
            Console.WriteLine($"Connector {connectorNumber}: status {status} accepted");
            return;
        }
        else if (reply.PayloadCase == ServerMessage.PayloadOneofCase.Error && reply.Error.Code == ErrorCode.RetryLater)
        {
            Console.WriteLine($"Connector {connectorNumber}: retry later, waiting {StatusRetrySeconds} s");
            await Task.Delay(TimeSpan.FromSeconds(StatusRetrySeconds));
        }
        else
        {
            throw new InvalidOperationException(
                $"Unexpected reply to status of connector {connectorNumber}: {reply}");
        }
    }
}