using Grpc.Core;
using Grpc.Net.Client;
using VoltGrid.Contracts.Station.V1;

const string GatewayAddress = "http://localhost:5205";
const string StationId = "ST-001";
const string StationModel = "Plus";
const string StationSerialNumber = "123";
const string StationFirmwareVersion = "1.0.5";

using var channel = GrpcChannel.ForAddress(GatewayAddress);
var client = new StationLink.StationLinkClient(channel);

try
{
    var headers = new Metadata { { "station-id", StationId } };
    using var call = client.Connect(headers);
    Console.WriteLine("Call opened");

    var bootMessage = new StationMessage
    {
        MessageId = "1",
        BootNotification = new BootNotification
        {
            Model = StationModel,
            SerialNumber = StationSerialNumber,
            FirmwareVersion = StationFirmwareVersion
        }
    };
    await call.RequestStream.WriteAsync(bootMessage);
    Console.WriteLine($"Sent BootNotification as message {bootMessage.MessageId}");

    if (!await call.ResponseStream.MoveNext(CancellationToken.None))
    {
        Console.WriteLine("Gateway closed the stream without a reply");
        return;
    }

    var reply = call.ResponseStream.Current;
    Console.WriteLine($"Reply to message {reply.MessageId}");

    switch (reply.PayloadCase)
    {
        case ServerMessage.PayloadOneofCase.BootNotificationResponse:
            var bootResponse = reply.BootNotificationResponse;
            Console.WriteLine(
                $"Boot {bootResponse.Status}, heartbeat interval {bootResponse.HeartbeatIntervalSeconds} s, " +
                $"gateway time {bootResponse.CurrentTime.ToDateTimeOffset()}");
            break;
        case ServerMessage.PayloadOneofCase.Error:
            Console.WriteLine($"Gateway refused the message: {reply.Error.Code}");
            break;
        default:
            Console.WriteLine($"Unexpected reply: {reply.PayloadCase}");
            break;
    }

    await call.RequestStream.CompleteAsync();
    var hasMore = await call.ResponseStream.MoveNext(CancellationToken.None);
    Console.WriteLine($"More messages after close: {hasMore}");
    Console.WriteLine($"Call finished with status {call.GetStatus().StatusCode}");
}
catch (RpcException ex)
{
    Console.WriteLine($"Call failed: {ex.StatusCode}, {ex.Status.Detail}");
}