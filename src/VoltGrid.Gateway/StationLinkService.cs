using Grpc.Core;
using VoltGrid.Contracts.Station.V1;

namespace VoltGrid.Gateway;

public class StationLinkService : StationLink.StationLinkBase
{
    public override Task Connect(
        IAsyncStreamReader<StationMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream, ServerCallContext context)
    {
        return Task.CompletedTask;
    }
}