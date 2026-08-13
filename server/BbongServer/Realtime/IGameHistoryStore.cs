using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BbongServer.Realtime;

/// <summary>
/// 게임 히스토리 영구 저장소(CS/디버깅용). 게임 메타 + 좌석별 참여자 + 라운드 이벤트 전체를 기록한다.
/// 방 루프는 fire-and-forget 직렬 체인으로 흘려보내고, 실패해도 게임 진행엔 영향이 없다.
/// </summary>
public interface IGameHistoryStore
{
    Task CreateGameAsync(GameRecord game);

    Task AppendEventsAsync(Guid gameId, IReadOnlyList<HistoryEvent> events);

    Task CompleteGameAsync(GameCompletion completion);
}

public sealed record GamePlayerRecord(int Seat, Guid? UserId, string Nickname, bool IsBot);

public sealed record GameRecord(
    Guid Id,
    string RoomCode,
    int Stake,
    int TargetPlayers,
    DateTimeOffset StartedAtUtc,
    IReadOnlyList<GamePlayerRecord> Players,
    GameMode Mode);

/// <summary>
/// 방이 만들어진 경로. 모든 게임에 남겨 데이터 추적에 쓰고, 전적 집계는 QuickMatch만 센다
/// (친구방은 상대를 직접 고를 수 있어 승패를 주고받기 쉽다).
/// </summary>
public enum GameMode
{
    Friend,
    QuickMatch
}

public sealed record GameCompletion(
    Guid GameId,
    DateTimeOffset EndedAtUtc,
    int[] WinnerSeats,
    int[] FinalDebts,
    IReadOnlyDictionary<int, long> PayoutsBySeat);
