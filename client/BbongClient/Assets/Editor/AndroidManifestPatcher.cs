using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace Bbong.Editor
{
    /// <summary>
    /// 개발 빌드에 한해 평문 HTTP를 허용한다. 안드로이드 9부터 기본 차단이라 에뮬레이터에서
    /// 호스트 PC의 개발 서버(http://10.0.2.2:5080)에 붙지 못한다. 배포 빌드는 손대지 않아
    /// HTTPS 강제가 그대로 유지된다.
    /// </summary>
    public sealed class AndroidManifestPatcher : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                return;
            }

            const string androidNs = "http://schemas.android.com/apk/res/android";
            var doc = new XmlDocument();
            doc.Load(manifestPath);

            if (doc.SelectSingleNode("/manifest/application") is not XmlElement application)
            {
                return;
            }

            // Android 13+에서 OnBackInvokedCallback을 쓰려면 앱이 명시적으로 opt-in해야 한다.
            // 선언이 없으면 콜백을 등록해도 시스템이 무시하고 구식 경로로 처리한다.
            application.SetAttribute("enableOnBackInvokedCallback", androidNs, "true");

            if (UnityEditor.EditorUserBuildSettings.development)
            {
                application.SetAttribute("usesCleartextTraffic", androidNs, "true");
                System.Console.WriteLine("[CliBuild] 개발 빌드 — 평문 HTTP 허용 적용");
            }

            doc.Save(manifestPath);
        }
    }
}
