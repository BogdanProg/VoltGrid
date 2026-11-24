namespace VoltGrid.Station.Simulator;

public record StationOptions(
    string StationId,
    string Model,
    string SerialNumber,
    string FirmwareVersion,
    IReadOnlyList<int> ConnectorNumbers);