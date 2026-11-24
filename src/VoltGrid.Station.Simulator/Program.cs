using Grpc.Net.Client;
using VoltGrid.Contracts.Station.V1;
using VoltGrid.Station.Simulator;

const string GatewayAddress = "http://localhost:5205";

var options = new StationOptions(
    StationId: "ST-001",
    Model: "Plus",
    SerialNumber: "123",
    FirmwareVersion: "1.0.5",
    ConnectorNumbers: [1, 2]);

using var channel = GrpcChannel.ForAddress(GatewayAddress);
var client = new StationLink.StationLinkClient(channel);

var station = new SimulatedStation(options, client);
await station.RunAsync();