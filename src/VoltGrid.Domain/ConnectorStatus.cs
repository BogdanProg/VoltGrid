namespace VoltGrid.Domain;

public enum ConnectorStatus
{
    Unknown = 0,
    Available = 1,
    Occupied = 2,
    Charging = 3,
    Faulted = 4,
    Unavailable = 5
}