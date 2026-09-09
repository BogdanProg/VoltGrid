namespace VoltGrid.Domain;

public class RfidTag
{
    public required string Id { get; init; }
    public required string OwnerName { get; init; }
    public bool IsBlocked { get; private set; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public void SetBlocked(bool blocked) => IsBlocked = blocked;
}