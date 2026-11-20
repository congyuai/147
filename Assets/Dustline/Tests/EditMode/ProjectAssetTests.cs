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
            Assert.That(catalog.Items.Count, Is.EqualTo(6));
            foreach (var item in catalog.Items)
                if (item.Kind == ItemKind.Weapon)
                {
                    Assert.That(item.Weapon, Is.Not.Null);
                    Assert.That(catalog.Find(item.Weapon.AmmoId).Kind, Is.EqualTo(ItemKind.Ammo));
                }
            foreach (var entry in loot.Entries) Assert.That(catalog.Find(entry.ItemId), Is.Not.Null);
        }

        [TestCase("rifle", "AR-20突击步枪")]
        [TestCase("ammo", "5.56毫米弹药")]
        [TestCase("medkit", "战地医疗包")]
        [TestCase("armor", "战术防弹衣")]
        [TestCase("circuit", "电路板")]
        [TestCase("relic", "勘测模块")]
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
        public void BootstrapSceneAndPipelineAreConfigured()
        {
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.Not.Null);
            Assert.That(EditorBuildSettings.scenes.Length, Is.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes[0].path), Is.Not.Null);
            Assert.That(Shader.Find("Universal Render Pipeline/Lit"), Is.Not.Null);
            Assert.That(Shader.Find("Universal Render Pipeline/Unlit"), Is.Not.Null);
        }
    }
}
