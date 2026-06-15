using System;

namespace BbongServer.Domain.Shop;

/// <summary>광고 보상 수령 기록 1건. 쿨다운·일일 제한 판정의 근거.</summary>
public sealed record AdRewardClaim(Guid UserId, AdRewardKind Kind, DateTimeOffset ClaimedAt);
