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

var lastMessageId = 0;

using var channel = GrpcChannel.ForAddress(GatewayAddress);
var client = new StationLink.StationLinkClient(channel);

try
{
    var headers = new Metadata { { "station-id", StationId } };
    using var call = client.Connect(headers);
    Console.WriteLine("Call opened");

    var bootResponse = await BootAsync(call);
    Console.WriteLine($"Station accepted, heartbeat interval {bootResponse.HeartbeatIntervalSeconds} s");

    await call.RequestStream.CompleteAsync();
    var hasMore = await call.ResponseStream.MoveNext(CancellationToken.None);
    Console.WriteLine($"More messages after close: {hasMore}");
    Console.WriteLine($"Call finished with status {call.GetStatus().StatusCode}");
}

catch (RpcException ex)
{
    Console.WriteLine($"Call failed: {ex.StatusCode}, {ex.Status.Detail}");
}

async Task<ServerMessage> SendAsync(
    AsyncDuplexStreamingCall<StationMessage, ServerMessage> call,
    StationMessage message)
{
    var messageId =  ++lastMessageId;
    message.MessageId = messageId.ToString();
    await call.RequestStream.WriteAsync(message);
    Console.WriteLine($"Sent message {messageId}: {message.PayloadCase}");
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