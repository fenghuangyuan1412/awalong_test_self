using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using Avalon.UI;

namespace Avalon.EditorTools
{
    /// <summary>一次性工程配置：PlayerSettings + 场景 + Android 工具链路径。batchmode 可调用。</summary>
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        public static void Setup()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            PlayerSettings.companyName = "AvalonPreview";
            PlayerSettings.productName = "阿瓦隆";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.avalon.preview");
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;

            // 复用工作区已有 Android SDK；NDK/OpenJDK 已放引擎默认目录自动检测
            const string sdkRoot = @"D:\ai\game_test_1\android-sdk";
            if (Directory.Exists(sdkRoot)) EditorPrefs.SetString("AndroidSdkRoot", sdkRoot);
            EditorPrefs.SetBool("AndroidUseEmbeddedJDK", true);
            EditorPrefs.SetBool("AndroidPreferGradleMaster", false);

            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("UIApp");
            go.AddComponent<UIApp>();
            var camGo = GameObject.Find("UICamera");
            if (camGo != null) Object.DestroyImmediate(camGo); // 相机由 UIApp 运行时创建，避免场景里重复
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Avalon] ProjectSetup done. scene=" + ScenePath);
        }

        public static void SetupFromCli() => Setup();
    }
}
