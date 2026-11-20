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
        const string PipelinePath = "Assets/Dustline/Settings/MobilePipeline.asset";
        const string Version = "0.2.0";
        static readonly Dictionary<string, string> ItemNames = new Dictionary<string, string> {
            { "rifle", "AR-20突击步枪" }, { "ammo", "5.56毫米弹药" },
            { "medkit", "战地医疗包" }, { "armor", "战术防弹衣" },
            { "circuit", "电路板" }, { "relic", "勘测模块" },
            { WeaponDefinition.BatonId, "检修短棍" }
        };
        static ProjectSetup() { EditorApplication.delayCall += PrepareIfMissing; }

        static void PrepareIfMissing()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += PrepareIfMissing; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(Root + "/Items.asset");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Dustline/Settings/MobileRenderer.asset");
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (catalog == null
                || renderer == null
                || AssetDatabase.LoadAssetAtPath<LootTable>(Root + "/Loot.asset") == null
                || pipeline == null || GraphicsSettings.defaultRenderPipeline != pipeline
                || !pipeline.supportsSoftShadows || pipeline.shadowCascadeCount != 2
                || pipeline.additionalLightsRenderingMode != LightRenderingMode.PerPixel
                || catalog.Find(WeaponDefinition.BatonId) == null
                || PlayerSettings.bundleVersion != Version)
            {
                Prepare();
                return;
            }
            foreach (var item in catalog.Items)
                if (ItemNames.TryGetValue(item.Id, out var displayName) && item.DisplayName != displayName)
                {
                    Prepare();
                    return;
                }
        }

        [MenuItem("Dustline/Prepare Project")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory("Assets/Dustline/Settings");
            AssetDatabase.Refresh();
            if (AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/NotoSansSC-Regular.otf") == null)
                throw new BuildFailedException("缺少随包中文字体，请恢复 NotoSansSC-Regular.otf 后重试。");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Dustline/Settings/MobileRenderer.asset");
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, "Assets/Dustline/Settings/MobileRenderer.asset");
            }
            ResourceReloader.ReloadAllNullIn(renderer, "Packages/com.unity.render-pipelines.universal");
            EditorUtility.SetDirty(renderer);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Dustline Mobile";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            pipeline.renderScale = 1f;
            pipeline.msaaSampleCount = 2;
            pipeline.supportsHDR = false;
            pipeline.shadowDistance = 82;
            pipeline.shadowCascadeCount = 2;
            pipeline.maxAdditionalLightsCount = 2;
            var pipelineSettings = new SerializedObject(pipeline);
            var rendererList = pipelineSettings.FindProperty("m_RendererDataList");
            if (rendererList != null)
            {
                rendererList.arraySize = Mathf.Max(1, rendererList.arraySize);
                rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            }
            pipelineSettings.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            pipelineSettings.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
            pipelineSettings.FindProperty("m_SoftShadowsSupported").boolValue = true;
            pipelineSettings.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            pipelineSettings.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = settings.FindProperty("m_AlwaysIncludedShaders");
            foreach (var shaderName in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Skybox/Procedural", "UI/Default" })
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
                    Item("rifle", ItemNames["rifle"], ItemKind.Weapon, 3, 2, 1, 3.2f, 240, new Color(0.28f,0.43f,0.52f)),
                    Item("ammo", ItemNames["ammo"], ItemKind.Ammo, 1, 1, 60, 0.012f, 2, new Color(0.68f,0.55f,0.24f)),
                    Item("medkit", ItemNames["medkit"], ItemKind.Medicine, 1, 2, 3, 0.4f, 40, new Color(0.58f,0.25f,0.24f)),
                    Item("armor", ItemNames["armor"], ItemKind.Armor, 2, 3, 1, 4.5f, 160, new Color(0.32f,0.45f,0.29f)),
                    Item("circuit", ItemNames["circuit"], ItemKind.Valuable, 2, 1, 2, 0.3f, 120, new Color(0.2f,0.55f,0.48f)),
                    Item("relic", ItemNames["relic"], ItemKind.Valuable, 2, 2, 1, 1.2f, 350, new Color(0.58f,0.36f,0.65f))
                };
                catalog.Items[0].Weapon = weapon;
                catalog.Items[2].HealAmount = 45;
                catalog.Items[3].DamageReduction = 0.35f;
                foreach (var item in catalog.Items) EditorUtility.SetDirty(item);
                AssetDatabase.CreateAsset(catalog, Root + "/Items.asset");
            }
            EnsureBaton(catalog);
            foreach (var item in catalog.Items)
                if (ItemNames.TryGetValue(item.Id, out var displayName) && item.DisplayName != displayName)
                {
                    item.DisplayName = displayName;
                    EditorUtility.SetDirty(item);
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
            EnsureBatonLoot(table);
            PlayerSettings.companyName = "Dustline";
            PlayerSettings.productName = "Dustline Extraction";
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = 3;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.dustline.extraction");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            AssetDatabase.SaveAssets();
        }

        static void EnsureBaton(ItemCatalog catalog)
        {
            const string id = WeaponDefinition.BatonId;
            var item = catalog.Find(id);
            if (item == null)
            {
                item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(Root + "/" + id + ".asset");
                if (item == null)
                    item = Item(id, ItemNames[id], ItemKind.Weapon, 2, 1, 1, 0.8f, 86, new Color(0.59f, 0.40f, 0.21f));
                catalog.Items.Add(item);
                EditorUtility.SetDirty(catalog);
            }
            if (item.Weapon == null)
            {
                var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Root + "/BatonWeapon.asset");
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<WeaponDefinition>();
                    definition.Melee = true;
                    definition.MagazineSize = 0;
                    definition.Damage = 28;
                    definition.FireInterval = 0.56f;
                    definition.Range = 1.55f;
                    definition.Automatic = false;
                    AssetDatabase.CreateAsset(definition, Root + "/BatonWeapon.asset");
                }
                item.Weapon = definition;
                EditorUtility.SetDirty(item);
            }
            catalog.RebuildIndex();
        }

        static void EnsureBatonLoot(LootTable table)
        {
            if (table == null || table.Entries == null) return;
            foreach (var entry in table.Entries)
                if (entry != null && entry.ItemId == WeaponDefinition.BatonId) return;
            table.Entries.Add(new LootEntry { ItemId = WeaponDefinition.BatonId, Min = 1, Max = 1, Weight = 1 });
            EditorUtility.SetDirty(table);
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
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = false;
            Build(BuildTarget.Android, "Builds/Android/dustline-extraction-v" + Version + ".apk");
        }
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
