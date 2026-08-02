using System;
using System.Threading.Tasks;
using BbongServer.Domain.Matches;

namespace BbongServer.Application;

/// <summary>매치 영속 저장소. 테스트는 인메모리, 운영은 EF Core + PostgreSQL.</summary>
public interface IMatchStore
{
    Task SaveAsync(Match match);

    Task<Match?> GetByIdAsync(Guid id);

    /// <summary>정산 등 상태 변경 반영.</summary>
    Task UpdateAsync(Match match);
}
