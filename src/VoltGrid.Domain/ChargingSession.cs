namespace VoltGrid.Domain;

public class ChargingSession
{
    private ChargingSession() {}
    
    public Guid Id { get; private set; }
    public DateTimeOffset StartTime { get; private set; }
    public DateTimeOffset? EndTime { get; private set; }
    public string StationId { get; private set; } = null!;
    public int ConnectorNumber{ get; private set;}
    public string TransactionId { get; private set; } = null!;
    public string RfidTagId { get; private set; } = null!;
    public Guid TariffId { get; private set;}
    public decimal PricePerKwh{ get; private set;}
    public decimal MeterStartWh{ get; private set;}
    public decimal? MeterStopWh { get; private set; }
    public ChargingSessionStatus Status { get; private set; }

    public static ChargingSession Start(string stationId,
        int connectorNumber,
        string transactionId, 
        string rfidTagId, 
        Guid tariffId,
        decimal pricePerKwh,
        decimal meterStartWh,
        DateTimeOffset at) => new()
    {   
        Id = Guid.CreateVersion7(),
        StartTime = at.ToUniversalTime(),
        Status = ChargingSessionStatus.Active,
        StationId = stationId,
        ConnectorNumber = connectorNumber,
        TransactionId = transactionId,
        RfidTagId = rfidTagId,
        TariffId = tariffId,
        PricePerKwh = pricePerKwh,
        MeterStartWh = meterStartWh
    };

    public void Finish( decimal meterStopWh, DateTimeOffset at)
    {
        if (Status != ChargingSessionStatus.Active)
            throw new InvalidOperationException("Cannot finish a charging session when it is inactive.");
        MeterStopWh = meterStopWh; 
        EndTime = at.ToUniversalTime();
        Status = ChargingSessionStatus.Completed;
    }
}