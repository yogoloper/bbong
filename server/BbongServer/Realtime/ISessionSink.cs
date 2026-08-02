using System;
using System.Threading.Tasks;

namespace BbongServer.Realtime;

/// <summary>연결 1개로의 송신 추상화. 운영=WebSocket, 테스트=기록용 Fake.</summary>
public interface ISessionSink
{
    Guid UserId { get; }

    Task SendAsync(object message);
}

/// <summary>방 참여자(연결 + 프로필 스냅샷).</summary>
public sealed record RoomMember(ISessionSink Sink, Guid UserId, string Nickname);
