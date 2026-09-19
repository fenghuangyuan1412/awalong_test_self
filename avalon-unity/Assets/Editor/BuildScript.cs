using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Avalon.EditorTools
{
    /// <summary>命令行打包入口：Tuanjie.exe -batchmode -executeMethod Avalon.EditorTools.BuildScript.BuildAndroid</summary>
    public static class BuildScript
    {
        public static void BuildAndroid()
        {
            ProjectSetup.Setup(); // 幂等：每次构建前统一配置
            var outPath = Environment.GetEnvironmentVariable("AVALON_APK_OUT")
                          ?? "D:/ai/game_test_1/release/avalon-unity-debug.apk";
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            };
            Debug.Log("[Avalon] Building APK -> " + outPath);
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[Avalon] Build result: " + report.summary.result +
                      ", size=" + report.summary.totalSize + " bytes");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
