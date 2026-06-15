using System;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Shop;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>EF Core 기반 광고 보상 기록 저장소(PostgreSQL).</summary>
public sealed class EfAdRewardStore : IAdRewardStore
{
    private readonly BbongDbContext _db;

    public EfAdRewardStore(BbongDbContext db) => _db = db;

    public async Task AppendAsync(AdRewardClaim claim)
    {
        _db.AdRewards.Add(AdRewardRow.From(claim));
        await _db.SaveChangesAsync();
    }

    public async Task<DateTimeOffset?> GetLastClaimAsync(Guid userId, AdRewardKind kind)
    {
        var rows = _db.AdRewards.Where(r => r.UserId == userId && r.Kind == kind);
        if (!await rows.AnyAsync())
        {
            return null;
        }

        return await rows.MaxAsync(r => r.ClaimedAt);
    }

    public async Task<int> CountSinceAsync(Guid userId, AdRewardKind kind, DateTimeOffset since) =>
        await _db.AdRewards.CountAsync(r => r.UserId == userId && r.Kind == kind && r.ClaimedAt >= since);
}
