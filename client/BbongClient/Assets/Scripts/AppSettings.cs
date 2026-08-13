using UnityEngine;

namespace Bbong.Client
{
    /// <summary>
    /// 기기에 저장되는 앱 설정. 소리를 끌 수 없는 게임은 공공장소에서 켜기 어렵고,
    /// 스토어 심사에서도 기본적인 제어로 본다.
    /// </summary>
    public static class AppSettings
    {
        private const string SoundKey = "bbong.sound";
        private const string VibrationKey = "bbong.vibration";

        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt(SoundKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(SoundKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Apply();
            }
        }

        public static bool VibrationOn
        {
            get => PlayerPrefs.GetInt(VibrationKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(VibrationKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>앱 시작과 설정 변경 시 호출. 소리는 전역 볼륨으로 한 번에 끈다.</summary>
        public static void Apply() => AudioListener.volume = SoundOn ? 1f : 0f;

        /// <summary>
        /// 짧은 햅틱 한 번. Handheld.Vibrate()는 0.5초쯤 통째로 울려서 매 턴 쓰기엔 너무 세다 —
        /// 안드로이드 Vibrator에 지속시간을 직접 넘겨 40ms 틱으로 만든다.
        /// 지원하지 않는 플랫폼에서는 조용히 무시된다.
        /// </summary>
        public static void Vibrate()
        {
            if (!VibrationOn)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator == null || !vibrator.Call<bool>("hasVibrator"))
                {
                    return;
                }

                using var effects = new AndroidJavaClass("android.os.VibrationEffect");
                using var effect = effects.CallStatic<AndroidJavaObject>(
                    "createOneShot", 40L, effects.GetStatic<int>("DEFAULT_AMPLITUDE"));
                vibrator.Call("vibrate", effect);
            }
            catch (System.Exception)
            {
                Handheld.Vibrate(); // 기기가 새 API를 안 받으면 기본 진동으로 물러난다
            }
#endif
        }
    }
}
