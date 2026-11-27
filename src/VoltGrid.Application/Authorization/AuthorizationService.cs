using VoltGrid.Domain;

namespace VoltGrid.Application.Authorization;

public class AuthorizationService
{
    private const int MaxTagLength = 20; // OCPP idTag limit, same as HasMaxLength(20) in RfidTagConfiguration

    private readonly IRfidTagRepository _tags;

    public AuthorizationService(IRfidTagRepository tags)
    {
        _tags = tags;
    }

    public async Task<AuthorizationDecision> AuthorizeAsync(string idTag, CancellationToken ct)
    {
        var tagId = idTag.Trim().ToUpperInvariant();
        if (tagId.Length == 0 || tagId.Length > MaxTagLength)
            return AuthorizationDecision.Invalid;

        var tag = await _tags.FindAsync(tagId, ct);
        if (tag is null)
            return AuthorizationDecision.Invalid;

        return tag.CheckAuthorization(DateTimeOffset.UtcNow);
    }
}