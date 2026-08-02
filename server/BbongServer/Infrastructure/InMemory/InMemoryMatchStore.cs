using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Matches;

namespace BbongServer.Infrastructure.InMemory;

/// <summary>인메모리 매치 저장소(테스트용). 운영은 EF Core + PostgreSQL.</summary>
public sealed class InMemoryMatchStore : IMatchStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Match> _matches = new();

    public Task SaveAsync(Match match)
    {
        lock (_gate)
        {
            _matches[match.Id] = match;
        }

        return Task.CompletedTask;
    }

    public Task<Match?> GetByIdAsync(Guid id)
    {
        lock (_gate)
        {
            return Task.FromResult(_matches.TryGetValue(id, out var match) ? match : null);
        }
    }

    public Task UpdateAsync(Match match)
    {
        lock (_gate)
        {
            _matches[match.Id] = match;
        }

        return Task.CompletedTask;
    }
}
