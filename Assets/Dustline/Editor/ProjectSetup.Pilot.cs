using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Dustline.Editor
{
    public static partial class ProjectSetup
    {
        const string PilotOutput = "Builds/Pilot/";
        const string PilotName = "dustline-extraction-v" + Version;

        [MenuItem("Dustline/Pilot/Build Android Client")]
        public static void BuildPilotAndroid() => BuildPilot(BuildTarget.Android, PilotOutput + "Android/" + PilotName + ".apk");

        [MenuItem("Dustline/Pilot/Build Windows Client")]
        public static void BuildPilotWindows() => BuildPilot(BuildTarget.StandaloneWindows64, PilotOutput + "Windows/" + PilotName + ".exe");

        static void BuildPilot(BuildTarget target, string output, bool server = false)
        {
            bool android = target == BuildTarget.Android;
            var group = android ? BuildTargetGroup.Android : BuildTargetGroup.Standalone;
            if (!BuildPipeline.IsBuildTargetSupported(group, target) || EditorUserBuildSettings.activeBuildTarget != target)
                throw new BuildFailedException("请先安装目标平台的构建模块并切换平台。");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene) == null)
                throw new BuildFailedException("联机试跑场景未导入。");
            if (!server)
            {
                var pipe = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                if (pipe == null || renderer == null || !UsesRenderer(pipe, renderer)
                    || AssetDatabase.LoadAssetAtPath<Material>(RenderingRoot + "/LitBase.mat") == null)
                    throw new BuildFailedException("请先执行 Dustline > Prepare Project，生成联机客户端的渲染资源。");
            }
            string outputDirectory = Path.GetDirectoryName(output);
            if (Directory.Exists(outputDirectory) && Directory.GetFileSystemEntries(outputDirectory).Length != 0)
                throw new BuildFailedException("试跑产物目录非空，请先移动旧产物再构建：" + outputDirectory);
            Directory.CreateDirectory(outputDirectory);
            string product = PlayerSettings.productName, version = PlayerSettings.bundleVersion;
            string applicationId = PlayerSettings.GetApplicationIdentifier(group);
            int androidCode = PlayerSettings.Android.bundleVersionCode;
            bool appBundle = EditorUserBuildSettings.buildAppBundle;
            try
            {
                PlayerSettings.productName = ProductName;
                PlayerSettings.bundleVersion = Version;
                PlayerSettings.SetApplicationIdentifier(group, ApplicationId);
                if (android)
                {
                    PlayerSettings.Android.bundleVersionCode = AndroidVersionCode;
                    EditorUserBuildSettings.buildAppBundle = false;
                }
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { Scene }, locationPathName = output, target = target, options = BuildOptions.None,
                    subtarget = server ? (int)StandaloneBuildSubtarget.Server : android ? 0 : (int)StandaloneBuildSubtarget.Player
                });
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
            }
            finally
            {
                PlayerSettings.productName = product;
                PlayerSettings.bundleVersion = version;
                PlayerSettings.SetApplicationIdentifier(group, applicationId);
                if (android)
                {
                    PlayerSettings.Android.bundleVersionCode = androidCode;
                    EditorUserBuildSettings.buildAppBundle = appBundle;
                }
            }
        }
    }
}
