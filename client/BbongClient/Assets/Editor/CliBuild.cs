using System;
using System.Linq;
using UnityEditor;
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

        private static string ArgAfter(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
