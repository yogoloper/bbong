using System;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Matches;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>EF Core 기반 매치 저장소(PostgreSQL).</summary>
public sealed class EfMatchStore : IMatchStore
{
    private readonly BbongDbContext _db;

    public EfMatchStore(BbongDbContext db) => _db = db;

    public async Task SaveAsync(Match match)
    {
        _db.Matches.Add(match);
        await _db.SaveChangesAsync();
    }

    public async Task<Match?> GetByIdAsync(Guid id) =>
        await _db.Matches.FirstOrDefaultAsync(m => m.Id == id);

    public async Task UpdateAsync(Match match)
    {
        var existing = await _db.Matches.FindAsync(match.Id);
        if (existing is not null && !ReferenceEquals(existing, match))
        {
            _db.Entry(existing).CurrentValues.SetValues(match);
        }

        await _db.SaveChangesAsync();
    }
}
