using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Bbong.Editor
{
    /// <summary>
    /// CLI 배치 빌드 진입점 — 에디터를 열지 않고 검증용 빌드를 뽑을 때 사용.
    /// 예) Unity -batchmode -quit -projectPath client/BbongClient \
    ///       -executeMethod Bbong.Editor.CliBuild.WebGL -logFile /tmp/unity-cli-build.log
    /// 출력 경로 기본값은 -buildPath <경로> 인자로 변경 가능.
    /// </summary>
    public static class CliBuild
    {
        public static void WebGL()
        {
            var path = ArgAfter("-buildPath") ?? "../Builds/webgl-cli";
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

            var report = BuildPipeline.BuildPlayer(scenes, path, BuildTarget.WebGL, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Console.WriteLine($"[CliBuild] FAILED: {report.summary.result}, errors={report.summary.totalErrors}");
                EditorApplication.Exit(1);
                return;
            }

            Console.WriteLine($"[CliBuild] OK: {path} ({report.summary.totalSize / 1024 / 1024}MB)");
        }

        /// <summary>
        /// Android APK 배치 빌드(실기기 검증용). 스토어 업로드용 AAB는 -aab 플래그로.
        /// 예) Unity -batchmode -quit -projectPath client/BbongClient \
        ///       -executeMethod Bbong.Editor.CliBuild.Android -buildPath ../Builds/bbong.apk
        /// </summary>
        public static void Android()
        {
            var wantsAab = Environment.GetCommandLineArgs().Contains("-aab");
            var path = ArgAfter("-buildPath") ?? (wantsAab ? "../Builds/bbong.aab" : "../Builds/bbong.apk");
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

            ApplyMobileSettings();
            EditorUserBuildSettings.buildAppBundle = wantsAab;

            // -development: 개발 서버(평문 HTTP) 접속 허용 + 프로파일러 연결. 스토어 빌드에는 쓰지 않는다.
            var development = Environment.GetCommandLineArgs().Contains("-development");
            EditorUserBuildSettings.development = development;
            var options = development ? BuildOptions.Development : BuildOptions.None;

            var report = BuildPipeline.BuildPlayer(scenes, path, BuildTarget.Android, options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Console.WriteLine($"[CliBuild] FAILED: {report.summary.result}, errors={report.summary.totalErrors}");
                EditorApplication.Exit(1);
                return;
            }

            Console.WriteLine($"[CliBuild] OK: {path} ({report.summary.totalSize / 1024 / 1024}MB)");
        }

        /// <summary>
        /// 스토어가 요구하는 최소 식별자·타깃 설정. 에디터에서 수동으로 맞춰 두는 대신
        /// 빌드 시점에 강제해, CLI와 에디터 빌드가 같은 값을 갖게 한다.
        /// </summary>
        private static void ApplyMobileSettings()
        {
            PlayerSettings.companyName = "Yogoloper";
            PlayerSettings.productName = "나이롱뽕";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.yogoloper.bbong");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // GameActivity에서는 기기 뒤로가기가 Unity 입력으로 전달되지 않아 나가기 확인이 동작하지 않는다.
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            // UnityWebRequest의 평문 HTTP 차단은 안드로이드 매니페스트 설정과 별개다.
            // 개발 빌드에서만 풀어 로컬 서버에 붙이고, 배포 빌드는 HTTPS만 허용한 채로 둔다.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;

            // 버전 코드는 스토어 업로드마다 증가해야 한다. -versionCode 로 CI/수동 지정.
            if (int.TryParse(ArgAfter("-versionCode"), out var code))
            {
                PlayerSettings.Android.bundleVersionCode = code;
            }

            var version = ArgAfter("-appVersion");
            if (!string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
            }
        }

        private static string ArgAfter(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
