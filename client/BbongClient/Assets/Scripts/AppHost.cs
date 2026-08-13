using UnityEngine;
using UnityEngine.InputSystem;

namespace Bbong.Client
{
    /// <summary>
    /// 화면 전환과 무관하게 살아 있는 앱 전역 컴포넌트. 화면 부트스트랩들은 생성·파괴를 반복하므로
    /// 기기 뒤로가기나 앱 생명주기처럼 "화면이 바뀌어도 계속 받아야 하는 신호"를 받을 자리가 필요하다.
    /// 씬을 건드리지 않도록 런타임 초기화로 스스로 만들어진다.
    /// </summary>
    internal sealed class AppHost : MonoBehaviour
    {
        /// <summary>Java 콜백은 UI 스레드에서 오므로 플래그만 세우고 처리는 Update에서 한다.</summary>
        private static volatile bool _backRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("AppHost", typeof(AppHost));
            DontDestroyOnLoad(go);
            Application.wantsToQuit += InterceptQuit;
        }

        /// <summary>
        /// 안드로이드 뒤로가기가 종료로 이어지는 경로를 가로챈다. 화면별 뒤로 동작이 있으면
        /// 그걸 실행하고 종료를 취소한다 — 진행 중인 판이 통째로 날아가는 걸 막는 마지막 방어선.
        /// </summary>
        private static bool InterceptQuit()
        {
            if (UiKit.BackAction == null)
            {
                return true;
            }

            UiKit.InvokeBack();
            return false;
        }

        private void Start()
        {
            AppSettings.Apply(); // 저장된 소리 설정을 실행 즉시 반영
            RegisterAndroidBackHandler();
        }

        /// <summary>
        /// 모바일은 알림·전화로 앱을 잠깐 벗어나는 일이 잦고, 그 사이 소켓이 조용히 끊긴다.
        /// 복귀 시점에 상태를 확인해 끊겨 있으면 알려준다 — 화면(게임 테이블)이 자리 복귀를 맡는다.
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused || !WsClient.HasInstance)
            {
                return;
            }

            WsClient.Instance.NotifyIfDropped();
        }

        /// <summary>
        /// Unity 입력으로는 안드로이드 뒤로가기를 못 받는다(Keyboard 장치는 있는데 Escape로 매핑되지 않고,
        /// 레거시 입력 활성화도 빌드에 반영되지 않음). 그래서 플랫폼 API에 콜백을 직접 등록한다.
        /// Android 13(API 33)부터의 OnBackInvokedCallback을 쓰고, 그 미만은 Unity 기본 동작에 맡긴다.
        /// </summary>
        private void RegisterAndroidBackHandler()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                if (version.GetStatic<int>("SDK_INT") < 33)
                {
                    Debug.Log("[BBONG] 뒤로가기: API 33 미만 — 기본 동작 사용");
                    return;
                }

                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                var callback = new BackInvokedCallback();

                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    using var dispatcher = activity.Call<AndroidJavaObject>("getOnBackInvokedDispatcher");
                    // Unity가 기본 우선순위(0)로 자기 콜백을 등록해 뒤로가기를 먹는다.
                    // PRIORITY_OVERLAY(1_000_000)로 등록해야 우리 처리가 먼저 불린다.
                    dispatcher.Call("registerOnBackInvokedCallback", 1_000_000, callback);
                    Debug.Log("[BBONG] 뒤로가기 콜백 등록됨");
                }));
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BBONG] 뒤로가기 콜백 등록 실패: {ex.Message}");
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class BackInvokedCallback : AndroidJavaProxy
        {
            public BackInvokedCallback() : base("android.window.OnBackInvokedCallback") { }

            // Java 인터페이스 메서드명 그대로여야 프록시가 연결된다.
            public void onBackInvoked() => _backRequested = true;
        }
#endif

        private void Update()
        {
            var pressed = _backRequested;
            _backRequested = false;

            // 데스크톱·에디터에서는 Esc로 같은 동작을 확인할 수 있게 유지한다.
            pressed |= Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

            if (pressed)
            {
                Debug.Log("[BBONG] 뒤로가기 입력 감지");
                UiKit.InvokeBack();
            }
        }
    }
}
