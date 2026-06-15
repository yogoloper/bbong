using System;
using System.Threading.Tasks;
using BbongServer.Domain.Shop;

namespace BbongServer.Application;

/// <summary>광고 보상 수령 기록 저장소(쿨다운·일일 제한 판정용).</summary>
public interface IAdRewardStore
{
    Task AppendAsync(AdRewardClaim claim);

    /// <summary>해당 종류의 마지막 수령 시각. 없으면 null.</summary>
    Task<DateTimeOffset?> GetLastClaimAsync(Guid userId, AdRewardKind kind);

    /// <summary>since 이후 해당 종류 수령 횟수.</summary>
    Task<int> CountSinceAsync(Guid userId, AdRewardKind kind, DateTimeOffset since);
}
