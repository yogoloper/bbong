namespace BbongServer.Domain.Wallet;

/// <summary>재화 변동 사유. 모든 원장 기록은 사유를 가진다(감사·분쟁 대응, architecture §5).</summary>
public enum LedgerReason
{
    Welcome,       // 신규 가입 초기 지급
    AdReward,      // 광고 시청 보상
    DailyGrant,    // 일일 무료 지급
    BankruptcyAid, // 파산 보너스(잔액 0 구제)
    StakeEscrow,   // 맞춤게임 입장료 차감
    StakePayout,   // 게임 1등 배당
    StakeRefund,   // 대기실 퇴장/입장 거절 환불
    Purchase       // IAP/PG 구매 적립(후속)
}
