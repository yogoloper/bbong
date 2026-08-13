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

            WriteMobileIndexHtml(path);
            Console.WriteLine($"[CliBuild] OK: {path} ({report.summary.totalSize / 1024 / 1024}MB)");
        }

        /// <summary>
        /// 기본 템플릿의 index.html을 모바일 고정 비율 페이지로 교체한다.
        /// 데스크톱 브라우저에서도 캔버스를 폰 화면 비율(20:9)로 레터박스해 고정 —
        /// 게임 UI가 모바일 기준으로 설계돼 있어, 브라우저 창 비율을 그대로 따르면
        /// 시야가 판마다 달라져 레이아웃 검증이 안 된다.
        /// </summary>
        private static void WriteMobileIndexHtml(string buildPath)
        {
            // 빌드 산출물 이름은 폴더명에서 온다(webgl-cli → webgl-cli.loader.js).
            // 대소문자까지 실제 파일에서 읽는다 — macOS는 대소문자를 구분하지 않아
            // 여기서 틀려도 로컬에선 돌고 Pages에서만 404가 난다.
            var buildDir = System.IO.Path.Combine(buildPath, "Build");
            var loader = System.IO.Directory.GetFiles(buildDir, "*.loader.js")[0];
            var stem = System.IO.Path.GetFileName(loader)
                .Replace(".loader.js", string.Empty);

            var html = @"<!DOCTYPE html>
<html lang=""ko"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
<title>나이롱뽕</title>
<link rel=""shortcut icon"" href=""TemplateData/favicon.ico"">
<style>
  html, body { margin: 0; padding: 0; height: 100%; background: #060b1c; overflow: hidden; }
  /* 폰 가로 화면(20:9) 고정 레터박스 — 창이 어떤 비율이어도 게임 시야는 같다 */
  #wrap { position: fixed; inset: 0; display: flex; align-items: center; justify-content: center; }
  #unity-canvas { width: min(100vw, calc(100vh * 20 / 9)); aspect-ratio: 20 / 9; background: #060b1c; }
  #loading { position: fixed; inset: 0; display: flex; flex-direction: column; gap: 14px;
             align-items: center; justify-content: center; color: #f0d896;
             font-family: sans-serif; letter-spacing: 0.2em; }
  #bar { width: 240px; height: 6px; background: #1a2447; border-radius: 3px; overflow: hidden; }
  #fill { width: 0; height: 100%; background: #f0d896; }
</style>
</head>
<body>
<div id=""wrap""><canvas id=""unity-canvas"" tabindex=""-1""></canvas></div>
<div id=""loading""><div>나이롱뽕</div><div id=""bar""><div id=""fill""></div></div></div>
<script src=""Build/{STEM}.loader.js""></script>
<script>
createUnityInstance(document.querySelector('#unity-canvas'), {
  arguments: [],
  dataUrl: 'Build/{STEM}.data.unityweb',
  frameworkUrl: 'Build/{STEM}.framework.js.unityweb',
  codeUrl: 'Build/{STEM}.wasm.unityweb',
  streamingAssetsUrl: 'StreamingAssets',
  companyName: 'Yogoloper',
  productName: '나이롱뽕',
  productVersion: '1.0',
  devicePixelRatio: window.devicePixelRatio
}, function (p) {
  document.querySelector('#fill').style.width = (p * 100) + '%';
}).then(function () {
  document.querySelector('#loading').style.display = 'none';
}).catch(function (e) { alert(e); });
</script>
</body>
</html>
".Replace("{STEM}", stem);

            System.IO.File.WriteAllText(System.IO.Path.Combine(buildPath, "index.html"), html);
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
