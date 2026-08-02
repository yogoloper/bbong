using System;
using BbongCore.Game;

namespace BbongServer.Domain.Matches;

/// <summary>
/// 싱글(봇전) 매치 애그리거트. 시작 시 판돈 에스크로, 종료 시 1회 정산(§9).
/// 정산 몫은 코어 StakePot.Share와 단일 출처 — 클라 표시와 서버 지급이 항상 일치.
/// </summary>
public sealed class Match
{
    private Match(Guid id, Guid userId, int stake, int playerCount,
        MatchStatus status, DateTimeOffset createdAt, DateTimeOffset? settledAt)
    {
        Id = id;
        UserId = userId;
        Stake = stake;
        PlayerCount = playerCount;
        Status = status;
        CreatedAt = createdAt;
        SettledAt = settledAt;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public int Stake { get; }

    public int PlayerCount { get; }

    public MatchStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? SettledAt { get; private set; }

    public static Match Start(Guid id, Guid userId, int stake, int playerCount, DateTimeOffset createdAt) =>
        new(id, userId, stake, playerCount, MatchStatus.InProgress, createdAt, settledAt: null);

    /// <summary>정산: 승리 시 몫(절사), 패배 시 0. 1회만 가능(이중 정산 방지).</summary>
    public int Settle(bool won, int winnersCount, DateTimeOffset now)
    {
        if (Status == MatchStatus.Settled)
        {
            throw new InvalidOperationException("이미 정산된 매치입니다.");
        }

        if (winnersCount < 1 || winnersCount > PlayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(winnersCount), winnersCount, "공동 1등 수는 1~인원수 사이여야 합니다.");
        }

        Status = MatchStatus.Settled;
        SettledAt = now;
        return won ? StakePot.Share(Stake, PlayerCount, winnersCount) : 0;
    }
}
