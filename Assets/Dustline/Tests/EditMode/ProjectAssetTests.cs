using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline.Tests
{
    public sealed class ProjectAssetTests
    {
        [Test]
        public void CatalogAndLootHaveResolvableReferences()
        {
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            var loot = Resources.Load<LootTable>("Dustline/Loot");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
            Assert.That(loot, Is.Not.Null);
            Assert.DoesNotThrow(catalog.RebuildIndex);
            Assert.That(catalog.Items.Count, Is.EqualTo(7));
            foreach (var item in catalog.Items)
                if (item.Kind == ItemKind.Weapon)
                {
                    Assert.That(item.Weapon, Is.Not.Null);
                    if (item.Weapon.Melee)
                    {
                        Assert.That(item.Weapon.MagazineSize, Is.Zero);
                        Assert.That(string.IsNullOrEmpty(item.Weapon.AmmoId), Is.True);
                    }
                    else Assert.That(catalog.Find(item.Weapon.AmmoId).Kind, Is.EqualTo(ItemKind.Ammo));
                }
            Assert.That(catalog.Find(WeaponDefinition.RifleId).Weapon.Form, Is.EqualTo(WeaponForm.Rifle));
            Assert.That(catalog.Find(WeaponDefinition.BatonId).Weapon.Form, Is.EqualTo(WeaponForm.Baton));
            Assert.That(loot.Entries.Exists(entry => entry != null && entry.ItemId == WeaponDefinition.BatonId), Is.True);
            Assert.That(RollProbability(loot, catalog, "relic"), Is.GreaterThan(0d));
        }

        [Test]
        public void HighValueLootHasGreaterRareDropProbabilityAndValidEntries()
        {
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            var ordinary = Resources.Load<LootTable>("Dustline/Loot");
            var highValue = Resources.Load<LootTable>("Dustline/HighValueLoot");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(highValue, Is.Not.SameAs(ordinary));
            double ordinaryRare = RollProbability(ordinary, catalog, "relic");
            double highValueRare = RollProbability(highValue, catalog, "relic");
            Assert.That(highValueRare, Is.GreaterThan(ordinaryRare * 2d));
            double ordinaryChest = 1d - System.Math.Pow(1d - ordinaryRare, ordinary.Rolls);
            double highValueChest = 1d - System.Math.Pow(1d - highValueRare, highValue.Rolls);
            Assert.That(highValueChest, Is.GreaterThan(ordinaryChest));
        }

        static double RollProbability(LootTable table, ItemCatalog catalog, string id)
        {
            Assert.That(table, Is.Not.Null, "Run Dustline > Prepare Project first.");
            Assert.That(table.Rolls, Is.Positive);
            Assert.That(table.Entries, Is.Not.Empty);
            int total = 0, matching = 0;
            foreach (var entry in table.Entries)
            {
                Assert.That(entry, Is.Not.Null);
                Assert.That(catalog.Find(entry.ItemId), Is.Not.Null, entry.ItemId);
                Assert.That(entry.Weight, Is.Positive, entry.ItemId);
                Assert.That(entry.Min, Is.Positive, entry.ItemId);
                Assert.That(entry.Max, Is.GreaterThanOrEqualTo(entry.Min), entry.ItemId);
                total += entry.Weight;
                if (entry.ItemId == id) matching += entry.Weight;
            }
            return (double)matching / total;
        }

        [Test]
        public void BoundBladesAreIndependentOfTradeableItemsAndDefaultToTangdao()
        {
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            var blades = Resources.Load<BladeCatalog>("Dustline/Blades");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(blades, Is.Not.Null, "Run Dustline > Prepare Project first.");
            Assert.That(blades.Blades.Length, Is.EqualTo(2));
            var tangdao = blades.Resolve(BladeCatalog.DefaultId);
            var shortBlade = blades.Resolve(BladeCatalog.ShortBladeId);
            Assert.That(tangdao, Is.Not.Null);
            Assert.That(shortBlade, Is.Not.Null);
            Assert.That(tangdao.Form, Is.EqualTo(WeaponForm.Tangdao));
            Assert.That(shortBlade.Form, Is.EqualTo(WeaponForm.ShortBlade));
            Assert.That(blades.Resolve(null), Is.SameAs(tangdao));
            Assert.That(blades.Resolve(""), Is.SameAs(tangdao));
            Assert.That(blades.Resolve("unknown-blade"), Is.SameAs(tangdao));
            Assert.That(tangdao.Range, Is.GreaterThan(shortBlade.Range));
            Assert.That(shortBlade.FireInterval, Is.LessThan(tangdao.FireInterval));
            foreach (var choice in blades.Blades)
            {
                Assert.That(choice.DisplayName, Is.Not.Empty);
                Assert.That(choice.Weapon.Melee, Is.True);
                Assert.That(choice.Weapon.MagazineSize, Is.Zero);
                Assert.That(string.IsNullOrEmpty(choice.Weapon.AmmoId), Is.True);
                Assert.That(catalog.Find(choice.Id), Is.Null);
                var emptyLoadout = new GridInventory(1, 1, 0f, catalog);
                Assert.That(emptyLoadout.TryAdd(choice.Id, 1, out _), Is.False);
                Assert.That(emptyLoadout.Equipped(EquipmentSlot.Weapon), Is.Null);
            }
        }

        [Test]
        public void BuildVersionAdvancesWithoutChangingSaveIdentity()
        {
            Assert.That(PlayerSettings.bundleVersion, Is.EqualTo("0.3.1"));
            Assert.That(PlayerSettings.Android.bundleVersionCode, Is.EqualTo(5));
            Assert.That(PlayerSettings.companyName, Is.EqualTo("Dustline"));
            Assert.That(PlayerSettings.productName, Is.EqualTo("Dustline Extraction"));
            Assert.That(PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android), Is.EqualTo("com.dustline.extraction"));
        }

        [TestCase("rifle", "AR-20突击步枪")]
        [TestCase("ammo", "5.56毫米弹药")]
        [TestCase("medkit", "战地医疗包")]
        [TestCase("armor", "战术防弹衣")]
        [TestCase("circuit", "电路板")]
        [TestCase("relic", "勘测模块")]
        [TestCase(WeaponDefinition.BatonId, "检修短棍")]
        public void ItemNamesUseSimplifiedChinese(string id, string expected)
        {
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Find(id).DisplayName, Is.EqualTo(expected));
        }

        [TestCase(ItemKind.Weapon, "武器")]
        [TestCase(ItemKind.Ammo, "弹药")]
        [TestCase(ItemKind.Medicine, "医疗品")]
        [TestCase(ItemKind.Armor, "护甲")]
        [TestCase(ItemKind.Valuable, "贵重物品")]
        public void ItemKindsUseSimplifiedChinese(ItemKind kind, string expected)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            try
            {
                item.Kind = kind;
                Assert.That(item.KindName, Is.EqualTo(expected));
            }
            finally { Object.DestroyImmediate(item); }
        }

        [Test]
        public void BundledFontCoversChineseTextInGameSources()
        {
            var font = Resources.Load<Font>("Dustline/Fonts/NotoSansSC-Regular");
            Assert.That(font, Is.Not.Null);
            var importer = (TrueTypeFontImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(font));
            Assert.That(importer.includeFontData, Is.True);
            Assert.That(importer.fontTextureCase, Is.EqualTo(FontTextureCase.Dynamic));
            var characters = new HashSet<char>();
            foreach (var directory in new[] { "Assets/Dustline/Runtime", "Assets/Dustline/Editor" })
                foreach (var file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                    foreach (Match match in Regex.Matches(File.ReadAllText(file), @"[\u3000-\u303f\u3400-\u9fff\uff00-\uffef]"))
                        characters.Add(match.Value[0]);
            foreach (char character in characters)
                Assert.That(font.HasCharacter(character), Is.True, "字体缺少字符：" + character);
        }

        [Test]
        public void HarborRastersAndScansUseNative2kAndKeepTheirImportSettings()
        {
            Check(PortRaster.Chart, false, false, false);
            Check(PortRaster.Ground, false, false, true);
            foreach (PortRaster.Finish finish in System.Enum.GetValues(typeof(PortRaster.Finish)))
            {
                Check(PortRaster.Surface(finish, false), true, false, false);
                Check(PortRaster.Surface(finish, true), true, true, false);
            }
            var shader = Shader.Find(PortRaster.SurfaceShader);
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);

            static void Check(string key, bool repeat, bool normal, bool mask)
            {
                var texture = Resources.Load<Texture2D>(key);
                Assert.That(texture, Is.Not.Null, key);
                Assert.That(texture.width, Is.EqualTo(2048), key);
                Assert.That(texture.height, Is.EqualTo(2048), key);
                Assert.That(texture.mipmapCount, Is.EqualTo(12), key);
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                Assert.That(width, Is.EqualTo(2048), key);
                Assert.That(height, Is.EqualTo(2048), key);
                Assert.That(importer.wrapMode, Is.EqualTo(repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp), key);
                Assert.That(importer.textureType, Is.EqualTo(normal ? TextureImporterType.NormalMap : TextureImporterType.Default), key);
                Assert.That(importer.sRGBTexture, Is.EqualTo(!normal), key);
                Assert.That(importer.ignoreMipmapLimit, Is.True, key);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear), key);
                Assert.That(importer.isReadable, Is.False, key);
                var android = importer.GetPlatformTextureSettings("Android");
                Assert.That(android.overridden, Is.True, key);
                Assert.That(android.maxTextureSize, Is.EqualTo(2048), key);
                Assert.That(android.format, Is.EqualTo(normal || mask ? TextureImporterFormat.ETC2_RGBA8 : TextureImporterFormat.ETC2_RGB4), key);
            }
        }

        [Test]
        public void BootstrapSceneAndPipelineAreConfigured()
        {
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.Not.Null);
            Assert.That(LayerMask.NameToLayer(FirstPersonView.LayerName), Is.EqualTo(FirstPersonView.Layer));
            Assert.That(EditorBuildSettings.scenes.Length, Is.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes[0].path), Is.Not.Null);
            Assert.That(Shader.Find("Universal Render Pipeline/Lit"), Is.Not.Null);
            Assert.That(Shader.Find("Universal Render Pipeline/Unlit"), Is.Not.Null);
        }
    }
}
