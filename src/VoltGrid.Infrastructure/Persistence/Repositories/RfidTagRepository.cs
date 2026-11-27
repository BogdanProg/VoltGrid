using Microsoft.EntityFrameworkCore;
using VoltGrid.Application.Authorization;
using VoltGrid.Domain;

namespace VoltGrid.Infrastructure.Persistence.Repositories;

public class RfidTagRepository : IRfidTagRepository
{
    private readonly IDbContextFactory<VoltGridDbContext> _dbFactory;

    public RfidTagRepository(IDbContextFactory<VoltGridDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<RfidTag?> FindAsync(string tagId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.RfidTags.FindAsync([tagId], ct);
    }
}