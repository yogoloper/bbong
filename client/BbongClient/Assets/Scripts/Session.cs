using UnityEngine;

namespace Bbong.Client
{
    /// <summary>로그인 세션 상태(메모리, Play 동안 유지). 서버 토큰·프로필·잔액 보관.</summary>
    public static class Session
    {
        public static string Token = "";
        public static string UserId = "";
        public static string Nickname = "";
        public static long Balance;
        public static bool IsGuest = true;

        public static bool IsLoggedIn => !string.IsNullOrEmpty(Token);

        private const string UserIdKey = "bbong.userId";
        private const string ResumeSecretKey = "bbong.resumeSecret";

        /// <summary>기기에 보관된 재개 자격. 액세스 토큰(60분)과 달리 만료가 없다.</summary>
        public static string SavedUserId => PlayerPrefs.GetString(UserIdKey, "");

        public static string SavedResumeSecret => PlayerPrefs.GetString(ResumeSecretKey, "");

        public static bool HasSavedCredentials =>
            !string.IsNullOrEmpty(SavedUserId) && !string.IsNullOrEmpty(SavedResumeSecret);

        /// <summary>
        /// 다음 실행에서 같은 계정으로 돌아오기 위한 자격 저장. 이걸 안 하면 앱을 껐다 켤 때마다
        /// 새 게스트가 만들어져 포인트와 전적이 사라진다.
        /// </summary>
        public static void SaveCredentials(string userId, string resumeSecret)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(resumeSecret))
            {
                return;
            }

            PlayerPrefs.SetString(UserIdKey, userId);
            PlayerPrefs.SetString(ResumeSecretKey, resumeSecret);
            PlayerPrefs.Save();
        }

        public static void ForgetCredentials()
        {
            PlayerPrefs.DeleteKey(UserIdKey);
            PlayerPrefs.DeleteKey(ResumeSecretKey);
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            Token = "";
            UserId = "";
            Nickname = "";
            Balance = 0;
            IsGuest = true;
        }
    }
}
