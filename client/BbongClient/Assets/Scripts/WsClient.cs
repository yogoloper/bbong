using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Bbong.Client
{
    /// <summary>
    /// 서버 실시간(/ws) 연결. BCL ClientWebSocket — 외부 에셋 없음.
    /// 수신은 백그라운드 스레드 → 큐 → Update 펌프로 메인 스레드에서 이벤트 발화
    /// (Unity API는 메인 스레드 전용이라 큐 경유 필수).
    /// </summary>
    public sealed class WsClient : MonoBehaviour
    {
        private static WsClient _instance;

        private ClientWebSocket _socket;
        private readonly ConcurrentQueue<string> _received = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private volatile bool _closed;
        private string _closeReason;
        private bool _connectedNotified;
        private Action _onConnected;
        private Action<string> _onError;

        /// <summary>수신 메시지(raw JSON, 메인 스레드). 화면 부트스트랩이 구독.</summary>
        public event Action<string> OnMessage;

        /// <summary>연결 종료(메인 스레드). 사유 문자열.</summary>
        public event Action<string> OnClosed;

        public static bool HasInstance => _instance != null;

        public static WsClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("WsClient");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<WsClient>();
                    Application.runInBackground = true; // 포커스 없어도 수신 펌프 유지(멀티 테스트 필수)
                }

                return _instance;
            }
        }

        public bool IsConnected => _socket is { State: WebSocketState.Open };

        /// <summary>화면 전환 중 수신 펌프 일시정지(다음 화면이 구독한 뒤 재개 — 메시지 유실 방지).</summary>
        public bool Paused { get; set; }

        /// <summary>연결 시작. 콜백은 메인 스레드(Update 펌프)에서 호출.</summary>
        public void Connect(Action onConnected, Action<string> onError)
        {
            if (IsConnected)
            {
                onConnected?.Invoke();
                return;
            }

            _onConnected = onConnected;
            _onError = onError;
            _closed = false;
            _closeReason = null;
            _connectedNotified = false;
            _ = ConnectAsync();
        }

        public void Send(object message)
        {
            if (!IsConnected)
            {
                return;
            }

            var json = JsonUtility.ToJson(message);
            _ = SendAsync(json);
        }

        public void Disconnect()
        {
            var socket = _socket;
            _socket = null;
            if (socket != null)
            {
                _ = socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }

        private async Task ConnectAsync()
        {
            try
            {
                _socket = new ClientWebSocket();
                _socket.Options.SetRequestHeader("Authorization", "Bearer " + Session.Token);
                var url = ServerApi.BaseUrl.Replace("http://", "ws://").Replace("https://", "wss://") + "/ws";
                await _socket.ConnectAsync(new Uri(url), CancellationToken.None);
                _ = ReceiveLoopAsync(_socket);
            }
            catch (Exception ex)
            {
                _closed = true;
                _closeReason = $"연결 실패: {ex.Message}";
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket)
        {
            var buffer = new byte[64 * 1024];
            using var stream = new MemoryStream();
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    stream.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _closed = true;
                            _closeReason = "서버가 연결을 종료했습니다.";
                            return;
                        }

                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    _received.Enqueue(Encoding.UTF8.GetString(stream.ToArray()));
                }
            }
            catch (Exception ex)
            {
                _closed = true;
                _closeReason = $"연결 끊김: {ex.Message}";
            }
        }

        private async Task SendAsync(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync();
            try
            {
                var socket = _socket;
                if (socket is { State: WebSocketState.Open })
                {
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void Update()
        {
            if (!_connectedNotified && IsConnected)
            {
                _connectedNotified = true;
                _onConnected?.Invoke();
            }

            while (!Paused && _received.TryDequeue(out var json))
            {
                OnMessage?.Invoke(json);
            }

            if (_closed)
            {
                _closed = false;
                var reason = _closeReason ?? "연결이 끊어졌습니다.";
                if (!_connectedNotified)
                {
                    _onError?.Invoke(reason);
                }
                else
                {
                    OnClosed?.Invoke(reason);
                }

                _socket = null;
                _connectedNotified = false;
            }
        }

        private void OnDestroy() => Disconnect();
    }
}
