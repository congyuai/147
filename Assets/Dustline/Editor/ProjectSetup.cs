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
    public static partial class ProjectSetup
    {
        const string Root = "Assets/Dustline/Resources/Dustline";
        const string Scene = "Assets/Dustline/Scenes/Bootstrap.unity";
        const string PipelinePath = "Assets/Dustline/Settings/MobilePipeline.asset";
        const string RendererPath = "Assets/Dustline/Settings/MobileRenderer.asset";
        const string RenderingRoot = Root + "/Rendering";
        const string Version = "0.5.0";
        const int AndroidVersionCode = 10;
        const int RaidRendererLayers = FirstPersonView.Mask | (1 << 29) | (1 << 30) | (1 << 2) | 1;
        static readonly string[] LitVariantAssets = { "LitBase", "LitMetal", "LitEmission", "LitMetalEmission" };
        static readonly Dictionary<string, string> ItemNames = new Dictionary<string, string> {
            { "rifle", "AR-20突击步枪" }, { "ammo", "5.56毫米弹药" },
            { "medkit", "战地医疗包" }, { "bandage", "急救绷带" }, { "trauma_case", "创伤急救盒" },
            { "armor", "战术防弹衣" },
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
            var blades = AssetDatabase.LoadAssetAtPath<BladeCatalog>(Root + "/Blades.asset");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (catalog == null
                || renderer == null || renderer.renderingMode != RenderingMode.Forward
                || (renderer.opaqueLayerMask.value & RaidRendererLayers) != RaidRendererLayers
                || (renderer.transparentLayerMask.value & RaidRendererLayers) != RaidRendererLayers
                || !PortTextureSetup.IsPrepared()
                || catalog.Find("pack_6") == null || catalog.Find("pack_compact")?.Storage.Capacity != 28 || catalog.Find("dust_lmg")?.Weapon == null
                || catalog.Find("rig_watch") == null || catalog.Items.Exists(item => (item.Kind == ItemKind.Rig || item.Kind == ItemKind.Pack) && item.Storage.WeightLimit <= 0f
                    || item.Kind == ItemKind.Rig && (item.Storage.Pockets == null || item.Storage.Pockets.Length == 0))
                || catalog.Find("surgery") == null || catalog.Find("part_sight_prism") == null
                || !LitVariantsPrepared()
                || AssetDatabase.LoadAssetAtPath<LootTable>(Root + "/Loot.asset") == null
                || AssetDatabase.LoadAssetAtPath<LootTable>(Root + "/HighValueLoot.asset") == null
                || blades == null || blades.Resolve(null)?.Form != WeaponForm.Tangdao
                || blades.Resolve(BladeCatalog.ShortBladeId)?.Form != WeaponForm.ShortBlade
                || pipeline == null || GraphicsSettings.defaultRenderPipeline != pipeline
                || !pipeline.supportsSoftShadows || pipeline.shadowCascadeCount != 2
                || !UsesRenderer(pipeline, renderer)
                || !UsesRenderer(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderingRoot + "/Low.asset"), renderer)
                || !UsesRenderer(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderingRoot + "/High.asset"), renderer)
                || pipeline.additionalLightsRenderingMode != LightRenderingMode.PerPixel
                || catalog.Find(WeaponDefinition.BatonId)?.Weapon?.Form != WeaponForm.Baton
                || PlayerSettings.bundleVersion != Version
                || PlayerSettings.Android.bundleVersionCode != AndroidVersionCode)
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
            Directory.CreateDirectory(RenderingRoot);
            Directory.CreateDirectory("Assets/Dustline/Settings");
            AssetDatabase.Refresh();
            if (LayerMask.NameToLayer(FirstPersonView.LayerName) != FirstPersonView.Layer)
                throw new BuildFailedException("请恢复 ProjectSettings/TagManager.asset 的第一人称渲染层。");
            if (AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/NotoSansSC-Regular.otf") == null)
                throw new BuildFailedException("缺少随包中文字体，请恢复 NotoSansSC-Regular.otf 后重试。");
            PortTextureSetup.Prepare();
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            ResourceReloader.ReloadAllNullIn(renderer, "Packages/com.unity.render-pipelines.universal");
            renderer.renderingMode = RenderingMode.Forward;
            renderer.opaqueLayerMask = renderer.opaqueLayerMask.value | RaidRendererLayers;
            renderer.transparentLayerMask = renderer.transparentLayerMask.value | RaidRendererLayers;
            EditorUtility.SetDirty(renderer);
            var pipeline = PrepareRendering(PipelinePath, "Dustline Balanced", renderer, 2, 2048, 83f, 2, 2);
            PrepareRendering(RenderingRoot + "/Low.asset", "Dustline Performance", renderer, 2, 1024, 51f, 2, 1);
            PrepareRendering(RenderingRoot + "/High.asset", "Dustline Detail", renderer, 4, 4096, 122f, 4, 3);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = settings.FindProperty("m_AlwaysIncludedShaders");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null || ShaderUtil.ShaderHasError(lit))
                throw new BuildFailedException("无法加载 URP Lit 着色器。");
            for (int i = shaders.arraySize - 1; i >= 0; i--)
                if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == lit)
                {
                    int length = shaders.arraySize;
                    shaders.DeleteArrayElementAtIndex(i);
                    if (shaders.arraySize == length) shaders.DeleteArrayElementAtIndex(i);
                }
            settings.FindProperty("m_FogStripping").intValue = 1;
            settings.FindProperty("m_FogKeepLinear").boolValue = true;
            settings.FindProperty("m_FogKeepExp").boolValue = false;
            settings.FindProperty("m_FogKeepExp2").boolValue = false;
            // Runtime meshes create these four local keyword combinations; retain them through referenced materials.
            for (int i = 0; i < LitVariantAssets.Length; i++) EnsureLitMaterial(LitVariantAssets[i], lit, (i & 1) != 0, (i & 2) != 0);
            foreach (var shaderName in new[] { "Universal Render Pipeline/Unlit", "Dustline/Harbor Sky", PortRaster.SurfaceShader, "Hidden/Dustline/Item Icon Alpha", "UI/Default" })
            {
                var shader = Shader.Find(shaderName);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                    throw new BuildFailedException("无法加载构建所需的着色器：" + shaderName);
                bool present = false;
                for (int i = 0; i < shaders.arraySize; i++) if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
                if (!present) { int i = shaders.arraySize++; shaders.GetArrayElementAtIndex(i).objectReferenceValue = shader; }
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(Root + "/Items.asset");
            if (catalog == null)
            {
                var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
                weapon.Form = WeaponForm.Rifle;
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
            EnsureMedicine(catalog);
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
            EnsureLootEntry(table, WeaponDefinition.BatonId, 1, 1, 1);
            EnsureLootEntry(table, "bandage", 1, 2, 7);
            EnsureLootEntry(table, "trauma_case", 1, 1, 1);
            EnsureHighValueLoot();
            PrepareEquipment(catalog, table, AssetDatabase.LoadAssetAtPath<LootTable>(Root + "/HighValueLoot.asset"));
            EnsureBoundBlades();
            PlayerSettings.companyName = "Dustline";
            PlayerSettings.productName = "Dustline Extraction";
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = AndroidVersionCode;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.dustline.extraction");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            AssetDatabase.SaveAssets();
        }

        static bool UsesRenderer(UniversalRenderPipelineAsset asset, UniversalRendererData renderer)
        {
            if (asset == null) return false;
            var serialized = new SerializedObject(asset);
            var renderers = serialized.FindProperty("m_RendererDataList");
            return renderers.arraySize > 0 && renderers.GetArrayElementAtIndex(0).objectReferenceValue == renderer
                && serialized.FindProperty("m_DefaultRendererIndex").intValue == 0;
        }

        static UniversalRenderPipelineAsset PrepareRendering(string path, string title, UniversalRendererData renderer,
            int samples, int atlas, float distance, int cascades, int localLights)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.name = title;
            asset.renderScale = 1f;
            asset.msaaSampleCount = samples;
            asset.supportsHDR = false;
            asset.supportsCameraOpaqueTexture = false;
            asset.shadowDistance = distance;
            asset.shadowCascadeCount = cascades;
            asset.cascadeBorder = 0.16f;
            asset.shadowDepthBias = 0.77f;
            asset.shadowNormalBias = 0.58f;
            asset.conservativeEnclosingSphere = true;
            asset.maxAdditionalLightsCount = localLights;
            asset.useSRPBatcher = true;

            var serialized = new SerializedObject(asset);
            var list = serialized.FindProperty("m_RendererDataList");
            list.arraySize = Mathf.Max(1, list.arraySize);
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
            serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            serialized.FindProperty("m_MainLightShadowmapResolution").intValue = atlas;
            serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
            serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            var split2 = serialized.FindProperty("m_Cascade2Split");
            if (split2 != null) split2.floatValue = 0.22f;
            var split4 = serialized.FindProperty("m_Cascade4Split");
            if (split4 != null) split4.vector3Value = new Vector3(0.095f, 0.235f, 0.50f);
            var softness = serialized.FindProperty("m_SoftShadowQuality");
            if (softness != null) softness.intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
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
            if (item.Weapon.Form != WeaponForm.Baton)
            {
                item.Weapon.Form = WeaponForm.Baton;
                EditorUtility.SetDirty(item.Weapon);
            }
            catalog.RebuildIndex();
        }

        static void EnsureLootEntry(LootTable table, string id, int min, int max, int weight)
        {
            if (table.Entries.Exists(entry => entry != null && entry.ItemId == id)) return;
            table.Entries.Add(new LootEntry { ItemId = id, Min = min, Max = max, Weight = weight });
            EditorUtility.SetDirty(table);
        }

        static void EnsureMedicine(ItemCatalog catalog)
        {
            Configure(catalog.Find("medkit"), MedicineForm.Pouch, 45f, 2.15f);
            Ensure("bandage", 1, 1, 5, 0.13f, 16, new Color(0.76f, 0.71f, 0.54f), MedicineForm.Roll, 22f, 1.15f);
            Ensure("trauma_case", 2, 1, 2, 0.62f, 78, new Color(0.70f, 0.27f, 0.22f), MedicineForm.Case, 70f, 3.05f);
            catalog.RebuildIndex();

            void Ensure(string id, int width, int height, int stack, float weight, int value, Color tint,
                MedicineForm form, float heal, float seconds)
            {
                var def = catalog.Find(id);
                if (def == null)
                {
                    def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(Root + "/" + id + ".asset")
                        ?? Item(id, ItemNames[id], ItemKind.Medicine, width, height, stack, weight, value, tint);
                    catalog.Items.Add(def);
                    EditorUtility.SetDirty(catalog);
                }
                Configure(def, form, heal, seconds);
            }

            static void Configure(ItemDefinition def, MedicineForm form, float heal, float seconds)
            {
                def.MedicineVisual = form;
                def.HealAmount = heal;
                def.UseSeconds = seconds;
                EditorUtility.SetDirty(def);
            }
        }

        static bool LitVariantsPrepared()
        {
            foreach (string name in LitVariantAssets)
                if (AssetDatabase.LoadAssetAtPath<Material>(RenderingRoot + "/" + name + ".mat") == null) return false;
            return true;
        }

        static void EnsureLitMaterial(string name, Shader shader, bool metal, bool emission)
        {
            string path = RenderingRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.enableInstancing = true;
            material.SetTexture("_MetallicGlossMap", metal ? AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Surfaces/Steel_albedo_2k.jpg") : null);
            material.SetColor("_EmissionColor", emission ? Color.white : Color.black);
            material.globalIlluminationFlags = emission ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            if (metal) material.EnableKeyword("_METALLICSPECGLOSSMAP");
            else material.DisableKeyword("_METALLICSPECGLOSSMAP");
            if (emission) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
        }

        static void EnsureHighValueLoot()
        {
            var table = AssetDatabase.LoadAssetAtPath<LootTable>(Root + "/HighValueLoot.asset");
            if (table != null) return;
            table = ScriptableObject.CreateInstance<LootTable>();
            table.Rolls = 3;
            table.Entries = new List<LootEntry> {
                new LootEntry { ItemId = "relic", Min = 1, Max = 1, Weight = 14 },
                new LootEntry { ItemId = "armor", Min = 1, Max = 1, Weight = 4 },
                new LootEntry { ItemId = "rifle", Min = 1, Max = 1, Weight = 3 },
                new LootEntry { ItemId = "circuit", Min = 1, Max = 2, Weight = 4 },
                new LootEntry { ItemId = "medkit", Min = 1, Max = 1, Weight = 3 },
                new LootEntry { ItemId = "ammo", Min = 20, Max = 45, Weight = 4 }
            };
            AssetDatabase.CreateAsset(table, Root + "/HighValueLoot.asset");
        }

        static void EnsureBoundBlades()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<BladeCatalog>(Root + "/Blades.asset");
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BladeCatalog>();
                AssetDatabase.CreateAsset(catalog, Root + "/Blades.asset");
            }
            catalog.Blades = new[] {
                new BladeChoice {
                    Id = BladeCatalog.DefaultId, DisplayName = "唐刀",
                    Weapon = PrepareBlade("TangdaoWeapon", WeaponForm.Tangdao, 40f, 0.68f, 2f)
                },
                new BladeChoice {
                    Id = BladeCatalog.ShortBladeId, DisplayName = "砂影短刀",
                    Weapon = PrepareBlade("ShortBladeWeapon", WeaponForm.ShortBlade, 28f, 0.42f, 1.48f)
                }
            };
            EditorUtility.SetDirty(catalog);
        }

        static WeaponDefinition PrepareBlade(string name, WeaponForm form, float damage, float interval, float range)
        {
            string path = Root + "/" + name + ".asset";
            var blade = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (blade == null)
            {
                blade = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(blade, path);
            }
            blade.Form = form;
            blade.Melee = true;
            blade.AmmoId = "";
            blade.MagazineSize = 0;
            blade.Damage = damage;
            blade.FireInterval = interval;
            blade.Range = range;
            blade.ReloadSeconds = 0f;
            blade.Recoil = 0f;
            blade.Automatic = false;
            EditorUtility.SetDirty(blade);
            return blade;
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
