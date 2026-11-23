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
var accepted = false;
var heartbeatIntervalSeconds = MinHeartbeatIntervalSeconds;

using var channel = GrpcChannel.ForAddress(GatewayAddress);
var client = new StationLink.StationLinkClient(channel);
var headers = new Metadata { { "station-id", StationId } };

while (true)
{
    try
    {
        Log("Connecting");
        using var call = client.Connect(headers);
        var bootedInThisCall = false;

        while (true)
        {
            try
            {
                if (!accepted)
                {
                    var bootResponse = await BootAsync(call);
                    heartbeatIntervalSeconds = Math.Max(bootResponse.HeartbeatIntervalSeconds, MinHeartbeatIntervalSeconds);
                    accepted = true;
                    bootedInThisCall = true;
                    Log($"Station accepted, heartbeat every {heartbeatIntervalSeconds} s");
                }
                else
                {
                    Log($"Reconnected without boot, heartbeat every {heartbeatIntervalSeconds} s");
                }

                foreach (var connectorNumber in connectorNumbers)
                {
                    await ReportStatusAsync(call, connectorNumber, ConnectorSentStatus.Available);
                }

                await HeartbeatLoopAsync(call, heartbeatIntervalSeconds);
            }
            catch (StationNotAcceptedException ex) when (!bootedInThisCall)
            {
                Log($"{ex.Message}, booting again");
                accepted = false;
            }
        }
    }
    catch (IOException ex)
    {
        Log($"Connection lost: {ex.GetType().Name}: {ex.Message}");
    }
    catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.Cancelled or StatusCode.Unknown)
    {
        Log($"Call failed: {ex.StatusCode}, {ex.Status.Detail}");
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
    {
        Log($"Gateway refused the station: {ex.Status.Detail}");
        return;
    }

    await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds));
}

void Log(string message) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} : {message}");

async Task<ServerMessage> SendAsync(
    AsyncDuplexStreamingCall<StationMessage, ServerMessage> call,
    StationMessage message)
{
    var messageId = ++lastMessageId;
    message.MessageId = messageId.ToString();
    await call.RequestStream.WriteAsync(message);
    Log($"Sent message {messageId}: {message.PayloadCase}");

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ReplyTimeoutSeconds));
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
            throw new InvalidOperationException($"Unexpected reply to boot: {reply}");
        }

        var bootResponse = reply.BootNotificationResponse;
        Log($"Boot {bootResponse.Status}, interval {bootResponse.HeartbeatIntervalSeconds} s");

        switch (bootResponse.Status)
        {
            case RegistrationStatus.Accepted:
                return bootResponse;

            case RegistrationStatus.Pending:
            case RegistrationStatus.Rejected:
                var retryAfterSeconds = Math.Max(bootResponse.HeartbeatIntervalSeconds, MinBootRetrySeconds);
                Log($"Retrying boot in {retryAfterSeconds} s");
                await Task.Delay(TimeSpan.FromSeconds(retryAfterSeconds));
                break;

            default:
                throw new InvalidOperationException($"Unexpected boot status: {bootResponse.Status}");
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
            Log($"Connector {connectorNumber}: status {status} accepted");
            return;
        }
        else if (reply.PayloadCase == ServerMessage.PayloadOneofCase.Error && reply.Error.Code == ErrorCode.RetryLater)
        {
            Log($"Connector {connectorNumber}: retry later, waiting {StatusRetrySeconds} s");
            await Task.Delay(TimeSpan.FromSeconds(StatusRetrySeconds));
        }
        else if (reply.PayloadCase == ServerMessage.PayloadOneofCase.Error && reply.Error.Code == ErrorCode.NotAccepted)
        {
            throw new StationNotAcceptedException($"Status of connector {connectorNumber} not accepted");
        }
        else
        {
            throw new InvalidOperationException($"Unexpected reply to status of connector {connectorNumber}: {reply}");
        }
    }
}

async Task HeartbeatLoopAsync(
    AsyncDuplexStreamingCall<StationMessage, ServerMessage> call,
    int intervalSeconds)
{
    var heartbeatMessage = new StationMessage { Heartbeat = new Heartbeat() };

    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
        var reply = await SendAsync(call, heartbeatMessage);

        if (reply.PayloadCase == ServerMessage.PayloadOneofCase.HeartbeatResponse)
        {
            Log($"Heartbeat accepted, server time {reply.HeartbeatResponse.CurrentTime.ToDateTimeOffset():HH:mm:ss}");
        }
        else if (reply.PayloadCase == ServerMessage.PayloadOneofCase.Error && reply.Error.Code == ErrorCode.NotAccepted)
        {
            throw new StationNotAcceptedException($"Heartbeat {reply.MessageId} not accepted");
        }
        else
        {
            throw new InvalidOperationException($"Unexpected reply to heartbeat: {reply}");
        }
    }
}

class StationNotAcceptedException(string message) : Exception(message) { }