namespace VoltGrid.Domain;

public class Connector
{
    public required string StationId { get; init; }
    public int Number { get; init; }
    public ConnectorStatus  Status { get; private set; }
    public ConnectorType Type { get; init; }
    public int MaxPowerWatts { get; init; }
    public required Guid TariffId { get; init; }
    public void ChangeStatus(ConnectorStatus newStatus) => Status = newStatus;
}