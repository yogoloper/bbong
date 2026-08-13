using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>게임 1판(세트) 메타.</summary>
[Table("games")]
public sealed class GameRow
{
    public Guid Id { get; set; }

    public string RoomCode { get; set; } = "";

    public int Stake { get; set; }

    public int TargetPlayers { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? EndedAtUtc { get; set; }

    /// <summary>우승 좌석 CSV("0,2"). null = 미완료(서버 재시작 등).</summary>
    public string? WinnerSeats { get; set; }

    /// <summary>방이 만들어진 경로(Friend | QuickMatch). 전적은 QuickMatch만 집계한다.</summary>
    public string Mode { get; set; } = "";
}

/// <summary>좌석별 참여자 — 유저별 게임 조회 인덱스.</summary>
[Table("game_players")]
[Index(nameof(UserId))]
[Index(nameof(GameId))]
public sealed class GamePlayerRow
{
    public long Id { get; set; }

    public Guid GameId { get; set; }

    public int Seat { get; set; }

    public Guid? UserId { get; set; }

    public string Nickname { get; set; } = "";

    public bool IsBot { get; set; }

    public int? FinalDebt { get; set; }

    public long Payout { get; set; }

    /// <summary>이 좌석이 우승했는지. 집계 때 우승 좌석 CSV를 파싱하지 않도록 확정해 둔다.</summary>
    public bool Won { get; set; }
}

/// <summary>라운드 진행 이벤트(딜 스냅샷·드로우·버림·뽕·족보·스톱·정산). 페이로드는 JSONB.</summary>
[Table("game_events")]
[Index(nameof(GameId), nameof(Id))]
public sealed class GameEventRow
{
    public long Id { get; set; }

    public Guid GameId { get; set; }

    public int RoundIndex { get; set; }

    public int? Seat { get; set; }

    public string Type { get; set; } = "";

    [Column(TypeName = "jsonb")]
    public string DataJson { get; set; } = "{}";

    public DateTimeOffset AtUtc { get; set; }
}
