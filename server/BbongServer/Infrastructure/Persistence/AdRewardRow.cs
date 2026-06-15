using System;
using BbongServer.Domain.Shop;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>광고 보상 수령 기록의 EF 영속 표현(도메인 AdRewardClaim과 변환).</summary>
public sealed class AdRewardRow
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public AdRewardKind Kind { get; set; }

    public DateTimeOffset ClaimedAt { get; set; }

    public static AdRewardRow From(AdRewardClaim claim) => new()
    {
        UserId = claim.UserId,
        Kind = claim.Kind,
        ClaimedAt = claim.ClaimedAt
    };
}
