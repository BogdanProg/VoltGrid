using VoltGrid.Domain;

namespace VoltGrid.Application.Authorization;

public interface IRfidTagRepository
{
    Task<RfidTag?> FindAsync(string tagId, CancellationToken ct);
}