namespace BbongServer.Domain.Shop;

/// <summary>광고 보상 종류.</summary>
public enum AdRewardKind
{
    Standard,    // 일반 보상: 2000P, 30분 쿨다운
    Bankruptcy   // 구제 보상: 10000P, 하루 3번, 잔액 파산 시에만
}
