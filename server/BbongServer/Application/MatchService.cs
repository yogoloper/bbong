using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BbongCore.Config;
using BbongServer.Domain.Matches;
using BbongServer.Domain.Wallet;

namespace BbongServer.Application;

/// <summary>
/// 싱글(봇전) 매치의 판돈 에스크로/정산. 결과 보고가 없으면 에스크로는 몰수 상태로 남는다(이탈 = 패배, §4-2).
/// </summary>
public sealed class MatchService
{
    private readonly ILedgerStore _ledger;
    private readonly IMatchStore _matches;
    private readonly IClock _clock;

    public MatchService(ILedgerStore ledger, IMatchStore matches, IClock clock)
    {
        _ledger = ledger;
        _matches = matches;
        _clock = clock;
    }

    /// <summary>매치 시작: 판돈 검증 후 에스크로 차감, 매치 생성.</summary>
    public async Task<(Guid MatchId, long Balance)> StartAsync(Guid userId, int stake, int playerCount)
    {
        if (!GameConfig.IsValidStake(stake))
        {
            throw new ArgumentException($"허용되지 않는 판돈입니다: {stake}", nameof(stake));
        }

        if (!GameConfig.IsValidPlayerCount(playerCount))
        {
            throw new ArgumentException($"인원은 {GameConfig.MinPlayers}~{GameConfig.MaxPlayers}명이어야 합니다: {playerCount}", nameof(playerCount));
        }

        var wallet = await _ledger.LoadWalletAsync(userId);
        var entry = wallet.Debit(stake, LedgerReason.StakeEscrow); // 잔액 부족 시 예외, 변동 없음
        await _ledger.AppendAsync(new[] { entry });

        var match = Match.Start(Guid.NewGuid(), userId, stake, playerCount, _clock.UtcNow);
        await _matches.SaveAsync(match);

        return (match.Id, wallet.Balance);
    }

    /// <summary>매치 정산: 승리 시 몫 적립(절사), 1회만. 남의 매치/없는 매치는 KeyNotFoundException.</summary>
    public async Task<(long Payout, long Balance)> SettleAsync(Guid userId, Guid matchId, bool won, int winnersCount)
    {
        var match = await _matches.GetByIdAsync(matchId);
        if (match is null || match.UserId != userId)
        {
            throw new KeyNotFoundException("매치를 찾을 수 없습니다.");
        }

        var payout = match.Settle(won, winnersCount, _clock.UtcNow);

        var wallet = await _ledger.LoadWalletAsync(userId);
        if (payout > 0)
        {
            var entry = wallet.Credit(payout, LedgerReason.StakePayout);
            await _ledger.AppendAsync(new[] { entry });
        }

        await _matches.UpdateAsync(match);

        return (payout, wallet.Balance);
    }
}
