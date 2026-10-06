namespace VoltGrid.Domain;

public class ChargingStation
{
    public required  string Id { get; init; }
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required Guid TarriffId { get; init; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public required string Model { get; init; }
    public required string SerialNumber { get; init; }
    public DateTimeOffset? LastHeartbeat { get; private set; }
    public void RecordHeartbeat(DateTimeOffset at) => LastHeartbeat = at.ToUniversalTime();
}