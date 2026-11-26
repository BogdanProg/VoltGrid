namespace VoltGrid.Domain;

public class RfidTag
{
    public required string Id { get; init; }
    public required string OwnerName { get; init; }
    public bool IsBlocked { get; private set; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public void SetBlocked(bool blocked) => IsBlocked = blocked;

    public AuthorizationDecision CheckAuthorization(DateTimeOffset now)
    {
        if (IsBlocked)
            return AuthorizationDecision.Blocked;

        if (ExpiresAt is not null && ExpiresAt <= now)
            return AuthorizationDecision.Expired;

        return AuthorizationDecision.Accepted;
    }
}