using System;
using System.Collections.Concurrent;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#endif

namespace Bbong.Client
{
    /// <summary>
    /// 서버 실시간(/ws) 연결. 기본은 BCL ClientWebSocket(외부 에셋 없음),
    /// WebGL은 브라우저 WebSocket(jslib 브리지) — 헤더 불가라 토큰은 쿼리로 전달.
    /// 수신은 큐 → Update 펌프로 메인 스레드에서 이벤트 발화.
    /// </summary>
    public sealed class WsClient : MonoBehaviour
    {
        private static WsClient _instance;

        private readonly ConcurrentQueue<string> _received = new();
        private volatile bool _closed;
        private string _closeReason;
        private bool _connectedNotified;
        private Action _onConnected;
        private Action<string> _onError;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void BbongWsConnect(string url);
        [DllImport("__Internal")] private static extern int BbongWsState();
        [DllImport("__Internal")] private static extern void BbongWsSend(string message);
        [DllImport("__Internal")] private static extern IntPtr BbongWsReceive();
        [DllImport("__Internal")] private static extern void BbongWsFree(IntPtr ptr);
        [DllImport("__Internal")] private static extern void BbongWsClose();

        private bool _webglStarted;
#else
        private const int ConnectTimeoutSeconds = 8;
        private ClientWebSocket _socket;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
#endif

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

#if UNITY_WEBGL && !UNITY_EDITOR
        public bool IsConnected => _webglStarted && BbongWsState() == 1;
#else
        public bool IsConnected => _socket is { State: WebSocketState.Open };
#endif

        /// <summary>화면 전환 중 수신 펌프 일시정지(다음 화면이 구독한 뒤 재개 — 메시지 유실 방지).</summary>
        public bool Paused { get; set; }

        private static string WsUrl => ServerApi.BaseUrl.Replace("http://", "ws://").Replace("https://", "wss://") + "/ws";

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
#if UNITY_WEBGL && !UNITY_EDITOR
            // 브라우저 WS는 Authorization 헤더 불가 → 서버가 허용하는 쿼리 토큰 사용
            BbongWsConnect($"{WsUrl}?access_token={Uri.EscapeDataString(Session.Token)}");
            _webglStarted = true;
#else
            _ = ConnectAsync();
#endif
        }

        public void Send(object message)
        {
            if (!IsConnected)
            {
                return;
            }

            var json = JsonUtility.ToJson(message);
#if UNITY_WEBGL && !UNITY_EDITOR
            BbongWsSend(json);
#else
            _ = SendAsync(json);
#endif
        }

        public void Disconnect()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_webglStarted)
            {
                _webglStarted = false;
                BbongWsClose();
            }
#else
            var socket = _socket;
            _socket = null;
            if (socket != null)
            {
                _ = socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>브리지 폴링: 수신 큐 옮기기 + 종료 감지(Update 초입에서 호출).</summary>
        private void PollWebGl()
        {
            if (!_webglStarted)
            {
                return;
            }

            for (var ptr = BbongWsReceive(); ptr != IntPtr.Zero; ptr = BbongWsReceive())
            {
                _received.Enqueue(Marshal.PtrToStringUTF8(ptr));
                BbongWsFree(ptr);
            }

            var state = BbongWsState();
            if (state >= 2)
            {
                _webglStarted = false;
                _closed = true;
                _closeReason = state == 3 ? "연결 실패 또는 끊김" : "서버가 연결을 종료했습니다.";
            }
        }
#else
        private async Task ConnectAsync()
        {
            try
            {
                _socket = new ClientWebSocket();
                _socket.Options.SetRequestHeader("Authorization", "Bearer " + Session.Token);

                // 네트워크가 끊긴 상태에서는 OS 타임아웃까지(수십 초) 매달린다.
                // 재접속 재시도가 그동안 멈춰 있으므로 직접 제한을 건다.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
                await _socket.ConnectAsync(new Uri(WsUrl), timeout.Token);
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
#endif

        /// <summary>
        /// 백그라운드에 있는 동안 끊긴 소켓은 종료 신호가 유실될 수 있다. 복귀 시점에
        /// 연결이 살아 있는지 확인해, 죽어 있으면 종료로 처리해 화면이 복구를 시작하게 한다.
        /// </summary>
        public void NotifyIfDropped()
        {
            if (_connectedNotified && !IsConnected)
            {
                _closeReason = "연결이 끊어졌습니다.";
                _closed = true;
            }
        }

        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            PollWebGl();
#endif
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

#if !UNITY_WEBGL || UNITY_EDITOR
                _socket = null;
#endif
                _connectedNotified = false;
            }
        }

        private void OnDestroy() => Disconnect();
    }
}
