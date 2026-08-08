using System;
using System.Collections.Concurrent;
using System.Linq;

namespace BbongServer.Realtime;

/// <summary>
/// 인메모리 방 레지스트리(단일 프로세스 전제 — 스케일아웃 시 Redis로 대체, considerations R7 인접 후속).
/// 초대코드 6자리 숫자 발급, userId → 방 역인덱스(끊김 라우팅용).
/// </summary>
public sealed class RoomRegistry
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly ConcurrentDictionary<Guid, Room> _byUser = new();

    /// <summary>방 생성 + 생성자 입장. runLoop=false는 테스트 전용(Execute 직접 구동). stake>0이면 맞춤게임.</summary>
    public Room Create(RoomMember creator, bool runLoop = true, int stake = 0, IStakeBank? bank = null)
    {
        string code;
        Room room;
        do
        {
            code = Random.Shared.Next(0, 1_000_000).ToString("D6");
            room = new Room(code, this, creator.UserId, stake, bank);
        }
        while (!_rooms.TryAdd(code, room));

        if (runLoop)
        {
            room.StartLoop();
        }

        room.Dispatch(new JoinCmd(creator));
        return room;
    }

    /// <summary>코드로 입장 시도. 방 없으면 false(정원/상태 검증은 방 루프에서).</summary>
    public bool TryJoin(string code, RoomMember member)
    {
        if (!_rooms.TryGetValue(code, out var room))
        {
            return false;
        }

        room.Dispatch(new JoinCmd(member));
        return true;
    }

    public Room? FindByUser(Guid userId) => _byUser.TryGetValue(userId, out var room) ? room : null;

    /// <summary>초대코드로 방 조회(입장 전 판돈 에스크로 판단용).</summary>
    public Room? FindByCode(string code) => _rooms.TryGetValue(code, out var room) ? room : null;

    /// <summary>
    /// 빠른매칭: 같은 조건(입장료·목표 인원)의 대기방 중 하나에 랜덤 배정, 없으면 새 방 생성(§9-1).
    /// 정원 도달 시 방이 자동 시작한다.
    /// </summary>
    public Room QuickMatch(RoomMember member, int stake, int players, IStakeBank? bank, bool runLoop = true)
    {
        var candidates = _rooms.Values
            .Where(r => r.Phase == RoomPhase.Waiting && r.Stake == stake && r.TargetPlayers == players
                        && r.Members.Count < players)
            .ToList();
        if (candidates.Count > 0)
        {
            var room = candidates[Random.Shared.Next(candidates.Count)];
            room.Dispatch(new JoinCmd(member));
            return room;
        }

        string code;
        Room created;
        do
        {
            code = Random.Shared.Next(0, 1_000_000).ToString("D6");
            created = new Room(code, this, member.UserId, stake, bank, targetPlayers: players);
        }
        while (!_rooms.TryAdd(code, created));

        if (runLoop)
        {
            created.StartLoop();
        }

        created.Dispatch(new JoinCmd(member));
        return created;
    }

    internal void Index(Guid userId, Room room) => _byUser[userId] = room;

    internal void Detach(Guid userId) => _byUser.TryRemove(userId, out _);

    internal void Remove(string code) => _rooms.TryRemove(code, out _);
}
