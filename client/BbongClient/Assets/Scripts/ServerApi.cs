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
            return "http://localhost:5080";
        }

        [Serializable] public class AuthResult { public string accessToken; public string userId; public string nickname; public bool isGuest; }
        [Serializable] public class MeResult { public string userId; public string nickname; public bool isGuest; public long balance; }
        [Serializable] private class BalanceResult { public long balance; }
        [Serializable] private class ErrorResult { public string error; }
        [Serializable] private class RenameBody { public string nickname; }
        [Serializable] private class AdBody { public string kind; }

        public static IEnumerator GuestLogin(Action onOk, Action<string> onErr) =>
            Send("POST", "/auth/guest", "{}", auth: false, text =>
            {
                var r = JsonUtility.FromJson<AuthResult>(text);
                Session.Token = r.accessToken;
                Session.UserId = r.userId;
                Session.Nickname = r.nickname;
                Session.IsGuest = r.isGuest;
                onOk();
            }, onErr);

        public static IEnumerator RefreshMe(Action onOk, Action<string> onErr) =>
            Send("GET", "/me", null, auth: true, text =>
            {
                var r = JsonUtility.FromJson<MeResult>(text);
                Session.Nickname = r.nickname;
                Session.Balance = r.balance;
                Session.IsGuest = r.isGuest;
                onOk();
            }, onErr);

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
            Action<string> onOk, Action<string> onErr)
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
            }
            else
            {
                onErr(ParseError(req));
            }
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
