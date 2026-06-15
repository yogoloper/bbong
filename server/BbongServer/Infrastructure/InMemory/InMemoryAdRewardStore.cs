using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Shop;

namespace BbongServer.Infrastructure.InMemory;

/// <summary>인메모리 광고 보상 기록 저장소(테스트용). 후속 EF Core + PostgreSQL.</summary>
public sealed class InMemoryAdRewardStore : IAdRewardStore
{
    private readonly object _gate = new();
    private readonly List<AdRewardClaim> _claims = new();

    public Task AppendAsync(AdRewardClaim claim)
    {
        lock (_gate)
        {
            _claims.Add(claim);
        }

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLastClaimAsync(Guid userId, AdRewardKind kind)
    {
        lock (_gate)
        {
            var times = _claims.Where(c => c.UserId == userId && c.Kind == kind).Select(c => c.ClaimedAt).ToList();
            return Task.FromResult(times.Count == 0 ? (DateTimeOffset?)null : times.Max());
        }
    }

    public Task<int> CountSinceAsync(Guid userId, AdRewardKind kind, DateTimeOffset since)
    {
        lock (_gate)
        {
            var count = _claims.Count(c => c.UserId == userId && c.Kind == kind && c.ClaimedAt >= since);
            return Task.FromResult(count);
        }
    }
}
