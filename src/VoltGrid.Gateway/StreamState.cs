using VoltGrid.Contracts.Station.V1;

namespace VoltGrid.Gateway;

public class StreamState
{
    public StreamState(string stationId)
    {
        StationId = stationId;
    }

    public string StationId { get; }
    
    public RegistrationStatus? BootStatus { get; set; }
    
    public bool AcceptedInStream => BootStatus == RegistrationStatus.Accepted;
    
    public bool RefusedInStream => BootStatus is RegistrationStatus.Rejected or RegistrationStatus.Pending;
}
