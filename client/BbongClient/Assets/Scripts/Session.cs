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
