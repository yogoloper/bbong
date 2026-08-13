using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Bbong.Client
{
    /// <summary>
    /// 서버(BbongServer) REST 호출. 코루틴 기반(StartCoroutine으로 호출).
    /// 개발 기본 URL은 localhost. 실기기 빌드 시 PC IP 또는 운영 주소로 교체.
    /// </summary>
    public static class ServerApi
    {
        // 개발: 에디터에서 로컬 서버. 실기기는 같은 네트워크 PC IP, 운영은 호스팅 도메인.
        public static string BaseUrl = ResolveBaseUrl();

        /// <summary>
        /// WebGL(GitHub Pages 등)에서는 페이지 URL의 ?server=https://... 로 서버를 지정할 수 있다
        /// — 재빌드 없이 임시 배포 서버 교체용. 그 외 플랫폼은 기본값.
        /// </summary>
        private static string ResolveBaseUrl()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var match = System.Text.RegularExpressions.Regex.Match(
                Application.absoluteURL ?? "", "[?&]server=([^&#]+)");
            if (match.Success)
            {
                return Uri.UnescapeDataString(match.Groups[1].Value).TrimEnd('/');
            }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            // 에뮬레이터에서 localhost는 기기 자신을 가리킨다. 10.0.2.2가 호스트 PC 루프백.
            return Debug.isDebugBuild ? "http://10.0.2.2:5080" : ProductionUrl;
#elif UNITY_IOS && !UNITY_EDITOR
            return Debug.isDebugBuild ? "http://localhost:5080" : ProductionUrl;
#else
            return "http://localhost:5080";
#endif
        }

        /// <summary>스토어 배포 빌드가 붙을 서버(웹처럼 ?server=로 갈아끼울 수 없다).</summary>
        private const string ProductionUrl = "https://bbong.fly.dev";

        [Serializable] public class AuthResult { public string accessToken; public string userId; public string nickname; public bool isGuest; public string resumeSecret; }
        [Serializable] private class ResumeBody { public string userId; public string resumeSecret; }
        [Serializable] public class MeResult { public string userId; public string nickname; public bool isGuest; public long balance; }
        [Serializable] private class BalanceResult { public long balance; }
        [Serializable] public class SeatCountStats { public int players; public int games; public int wins; public int winRate; public long totalWinnings; }
        [Serializable] public class ModeStats { public string mode; public int games; public int wins; public int winRate; public long totalWinnings; public SeatCountStats[] byPlayers; }
        [Serializable] public class StatsResult { public ModeStats[] modes; }
        [Serializable] public class HistoryEntry { public string endedAt; public string mode; public int players; public int stake; public bool won; public long payout; public int rank; public int humans; public string[] opponents; }
        [Serializable] private class HistoryWrap { public HistoryEntry[] items; }
        [Serializable] private class ErrorResult { public string error; }
        [Serializable] private class RenameBody { public string nickname; }
        [Serializable] private class AdBody { public string kind; }

        public static IEnumerator GuestLogin(Action onOk, Action<string> onErr) =>
            Send("POST", "/auth/guest", "{}", auth: false, text =>
            {
                var r = JsonUtility.FromJson<AuthResult>(text);
                Apply(r);
                Session.SaveCredentials(r.userId, r.resumeSecret); // 다음 실행에서 같은 계정으로 복귀
                onOk();
            }, onErr);

        /// <summary>
        /// 기기에 보관된 자격으로 계정 복귀. 자격이 거부되면(계정 삭제 등) onErr로 알리고,
        /// 호출자가 저장분을 버린 뒤 새 게스트를 만들지 결정한다.
        /// </summary>
        public static IEnumerator ResumeLogin(Action onOk, Action<string> onErr)
        {
            var body = JsonUtility.ToJson(new ResumeBody
            {
                userId = Session.SavedUserId,
                resumeSecret = Session.SavedResumeSecret
            });

            return Send("POST", "/auth/resume", body, auth: false, text =>
            {
                Apply(JsonUtility.FromJson<AuthResult>(text));
                onOk();
            }, onErr);
        }

        private static void Apply(AuthResult r)
        {
            Session.Token = r.accessToken;
            Session.UserId = r.userId;
            Session.Nickname = r.nickname;
            Session.IsGuest = r.isGuest;
        }

        public static IEnumerator RefreshMe(Action onOk, Action<string> onErr) =>
            Send("GET", "/me", null, auth: true, text =>
            {
                var r = JsonUtility.FromJson<MeResult>(text);
                Session.Nickname = r.nickname;
                Session.Balance = r.balance;
                Session.IsGuest = r.isGuest;
                onOk();
            }, onErr);

        /// <summary>내 전적(맞춤게임 기준). 서버가 집계 규칙을 갖고 있어 클라는 표시만 한다.</summary>
        public static IEnumerator FetchStats(Action<StatsResult> onOk, Action<string> onErr) =>
            Send("GET", "/me/stats", null, auth: true,
                text => onOk(JsonUtility.FromJson<StatsResult>(text)), onErr);

        /// <summary>
        /// 최근 게임 기록. 서버가 JSON 배열을 그대로 주는데 JsonUtility는 최상위 배열을 못 읽어
        /// 객체로 한 번 감싸서 파싱한다.
        /// </summary>
        public static IEnumerator FetchHistory(int limit, Action<HistoryEntry[]> onOk, Action<string> onErr) =>
            Send("GET", $"/me/history?limit={limit}", null, auth: true,
                text => onOk(JsonUtility.FromJson<HistoryWrap>("{\"items\":" + text + "}").items
                             ?? Array.Empty<HistoryEntry>()),
                onErr);

        public static IEnumerator Rename(string nickname, Action onOk, Action<string> onErr)
        {
            var body = JsonUtility.ToJson(new RenameBody { nickname = nickname });
            return Send("PATCH", "/me/nickname", body, auth: true, _ =>
            {
                Session.Nickname = nickname;
                onOk();
            }, onErr);
        }

        public static IEnumerator ClaimAdReward(string kind, Action<long> onOk, Action<string> onErr)
        {
            var body = JsonUtility.ToJson(new AdBody { kind = kind });
            return Send("POST", "/shop/ad-reward", body, auth: true, text =>
            {
                var r = JsonUtility.FromJson<BalanceResult>(text);
                Session.Balance = r.balance;
                onOk(r.balance);
            }, onErr);
        }

        private static IEnumerator Send(string method, string path, string body, bool auth,
            Action<string> onOk, Action<string> onErr, bool retried = false)
        {
            using var req = new UnityWebRequest(BaseUrl + path, method)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };

            if (body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.SetRequestHeader("Content-Type", "application/json");
            }

            if (auth && !string.IsNullOrEmpty(Session.Token))
            {
                req.SetRequestHeader("Authorization", "Bearer " + Session.Token);
            }

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                onOk(req.downloadHandler.text);
                yield break;
            }

            // 액세스 토큰은 60분짜리다. 앱을 오래 켜두면 그냥 끊기므로, 저장된 재개 자격으로
            // 토큰을 새로 받아 한 번만 다시 시도한다(재시도는 1회 — 무한 루프 방지).
            if (auth && req.responseCode == 401 && Session.HasSavedCredentials && !retried)
            {
                var refreshed = false;
                yield return ResumeLogin(() => refreshed = true, _ => { });
                if (refreshed)
                {
                    yield return Send(method, path, body, auth, onOk, onErr, retried: true);
                    yield break;
                }
            }

            onErr(ParseError(req));
        }

        private static string ParseError(UnityWebRequest req)
        {
            var text = req.downloadHandler?.text;
            if (!string.IsNullOrEmpty(text) && text.Contains("error"))
            {
                try { return JsonUtility.FromJson<ErrorResult>(text).error; }
                catch { /* fall through */ }
            }

            return string.IsNullOrEmpty(req.error) ? "서버 연결 실패" : req.error;
        }
    }
}
