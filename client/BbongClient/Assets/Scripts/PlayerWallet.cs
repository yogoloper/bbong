using UnityEngine;

namespace Bbong.Client
{
    /// <summary>
    /// 내 보유 재화(세션 한정, rules.md §9). 시작 10,000.
    /// 게임 시작 시 판돈 차감(에스크로), 게임(5판) 종료 시 1등 배당 입금.
    /// Phase 4에서 서버 지갑(원장)으로 대체 예정.
    /// </summary>
    public static class PlayerWallet
    {
        public const int StartingBalance = 10_000;

        public static int Balance { get; private set; } = StartingBalance;

        public static bool CanAfford(int amount) => Balance >= amount;

        public static void Pay(int amount) => Balance -= amount;

        public static void Receive(int amount) => Balance += amount;

        // Enter Play Mode Options(도메인 리로드 꺼짐)에서도 Play마다 초기화 보장
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Balance = StartingBalance;
    }
}
