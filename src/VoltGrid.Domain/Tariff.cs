namespace VoltGrid.Domain;

public class Tariff
{
    public Guid  Id { get;  init; }
    public required string Name { get; init; }
    public decimal PricePerKwh { get; init; }
    public required string Currency { get; init; }
    public bool IsActive { get; private set; }
    public void SetActive(bool active) => IsActive = active;
}