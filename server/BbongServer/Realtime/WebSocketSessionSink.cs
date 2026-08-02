using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BbongServer.Realtime;

/// <summary>WebSocket 1연결 송신. 동시 SendAsync 금지 제약 → 세마포어로 직렬화.</summary>
public sealed class WebSocketSessionSink : ISessionSink
{
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WebSocketSessionSink(Guid userId, WebSocket socket)
    {
        UserId = userId;
        _socket = socket;
    }

    public Guid UserId { get; }

    public async Task SendAsync(object message)
    {
        var bytes = Encoding.UTF8.GetBytes(RealtimeJson.Serialize(message));
        await _sendLock.WaitAsync();
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
