using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Dustline.Editor
{
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        const string Root = "Assets/Dustline/Resources/Dustline";
        const string Scene = "Assets/Dustline/Scenes/Bootstrap.unity";
        static ProjectSetup() { EditorApplication.delayCall += PrepareIfMissing; }

        static void PrepareIfMissing()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += PrepareIfMissing; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<ItemCatalog>(Root + "/Items.asset") == null
                || AssetDatabase.LoadAssetAtPath<LootTable>(Root + "/Loot.asset") == null
                || GraphicsSettings.defaultRenderPipeline == null) Prepare();
        }

        [MenuItem("Dustline/Prepare Project")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory("Assets/Dustline/Settings");
            AssetDatabase.Refresh();
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Dustline/Settings/MobileRenderer.asset");
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, "Assets/Dustline/Settings/MobileRenderer.asset");
            }
            ResourceReloader.ReloadAllNullIn(renderer, "Packages/com.unity.render-pipelines.universal");
            EditorUtility.SetDirty(renderer);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Dustline/Settings/MobilePipeline.asset");
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Dustline Mobile";
                pipeline.renderScale = 1;
                pipeline.msaaSampleCount = 2;
                pipeline.supportsHDR = false;
                pipeline.shadowDistance = 45;
                pipeline.shadowCascadeCount = 1;
                var pipelineSettings = new SerializedObject(pipeline);
                pipelineSettings.FindProperty("m_MainLightShadowmapResolution").intValue = 1024;
                pipelineSettings.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.Disabled;
                pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(pipeline, "Assets/Dustline/Settings/MobilePipeline.asset");
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = settings.FindProperty("m_AlwaysIncludedShaders");
            foreach (var shaderName in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "UI/Default" })
            {
                var shader = Shader.Find(shaderName);
                bool present = false;
                for (int i = 0; i < shaders.arraySize; i++) if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
                if (!present) { int i = shaders.arraySize++; shaders.GetArrayElementAtIndex(i).objectReferenceValue = shader; }
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(Root + "/Items.asset");
            if (catalog == null)
            {
                var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
                weapon.AmmoId = "ammo";
                weapon.MagazineSize = 20;
                weapon.Damage = 26;
                weapon.FireInterval = 0.14f;
                weapon.Range = 100;
                weapon.ReloadSeconds = 2;
                weapon.Recoil = 1.2f;
                weapon.Automatic = true;
                AssetDatabase.CreateAsset(weapon, Root + "/RifleWeapon.asset");
                catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                catalog.Items = new List<ItemDefinition> {
                    Item("rifle", "AR-20", ItemKind.Weapon, 3, 2, 1, 3.2f, 240, new Color(0.28f,0.43f,0.52f)),
                    Item("ammo", "5.56 ammo", ItemKind.Ammo, 1, 1, 60, 0.012f, 2, new Color(0.68f,0.55f,0.24f)),
                    Item("medkit", "Field medkit", ItemKind.Medicine, 1, 2, 3, 0.4f, 40, new Color(0.58f,0.25f,0.24f)),
                    Item("armor", "Field vest", ItemKind.Armor, 2, 3, 1, 4.5f, 160, new Color(0.32f,0.45f,0.29f)),
                    Item("circuit", "Circuit board", ItemKind.Valuable, 2, 1, 2, 0.3f, 120, new Color(0.2f,0.55f,0.48f)),
                    Item("relic", "Survey module", ItemKind.Valuable, 2, 2, 1, 1.2f, 350, new Color(0.58f,0.36f,0.65f))
                };
                catalog.Items[0].Weapon = weapon;
                catalog.Items[2].HealAmount = 45;
                catalog.Items[3].DamageReduction = 0.35f;
                foreach (var item in catalog.Items) EditorUtility.SetDirty(item);
                AssetDatabase.CreateAsset(catalog, Root + "/Items.asset");
            }
            var table = AssetDatabase.LoadAssetAtPath<LootTable>(Root + "/Loot.asset");
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<LootTable>();
                table.Rolls = 3;
                table.Entries = new List<LootEntry> {
                    new LootEntry { ItemId="ammo", Min=12, Max=35, Weight=5 },
                    new LootEntry { ItemId="medkit", Min=1, Max=1, Weight=3 },
                    new LootEntry { ItemId="circuit", Min=1, Max=2, Weight=3 },
                    new LootEntry { ItemId="relic", Min=1, Max=1, Weight=1 },
                    new LootEntry { ItemId="armor", Min=1, Max=1, Weight=1 },
                    new LootEntry { ItemId="rifle", Min=1, Max=1, Weight=1 }
                };
                AssetDatabase.CreateAsset(table, Root + "/Loot.asset");
            }
            PlayerSettings.companyName = "Dustline";
            PlayerSettings.productName = "Dustline Extraction";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.dustline.extraction");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            AssetDatabase.SaveAssets();
        }

        static ItemDefinition Item(string id, string title, ItemKind kind, int width, int height, int max, float weight, int value, Color tint)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.Id = id; item.DisplayName = title; item.Kind = kind;
            item.Width = width; item.Height = height; item.MaxStack = max;
            item.UnitWeight = weight; item.Value = value; item.Tint = tint;
            AssetDatabase.CreateAsset(item, Root + "/" + id + ".asset");
            return item;
        }

        [MenuItem("Dustline/Build Windows")]
        public static void BuildWindows() { Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Dustline.exe"); }
        [MenuItem("Dustline/Build Android")]
        public static void BuildAndroid() { Build(BuildTarget.Android, "Builds/Android/dustline-extraction-v0.1.0.apk"); }
        static void Build(BuildTarget target, string output)
        {
            Prepare();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { Scene }, locationPathName = output, target = target, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
        }
    }
}
